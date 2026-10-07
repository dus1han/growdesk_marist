using DoctorCrm.Api.Data;
using DoctorCrm.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace DoctorCrm.Api.Services;

public enum BillingState
{
    /// <summary>Stripe is not configured: no reminders, never blocked.</summary>
    Disabled,

    /// <summary>No subscription yet and nothing due (yet).</summary>
    NotSubscribed,

    /// <summary>Paid up.</summary>
    Active,

    /// <summary>A payment is overdue; the system still works until the grace period runs out.</summary>
    PaymentDue,

    /// <summary>The grace period is over and the last automatic charge is about to be tried.</summary>
    FinalAttempt,

    /// <summary>Unpaid after the grace period (and the last charge failed): only payment is possible.</summary>
    Blocked,
}

public enum BillingDueReason
{
    /// <summary>Stripe could not charge the card for a renewal.</summary>
    PaymentFailed,

    /// <summary>The clinic has never subscribed and the first payment date has passed.</summary>
    NotSubscribed,

    /// <summary>The subscription was cancelled or ended (e.g. Stripe cancelled it after failed retries).</summary>
    Ended,
}

public record BillingEvaluation(
    BillingState State,
    BillingDueReason? Reason = null,
    DateTime? DueSince = null,
    DateTime? BlockAt = null,
    int? DaysLeft = null)
{
    public bool IsBlocked => State == BillingState.Blocked;
}

/// <summary>
/// The subscription rules in one place. A missed payment starts a grace period of
/// <see cref="BillingConfig.GraceDays"/> days. When it runs out the card on file is charged one
/// last time; if that fails too, the system stays blocked until the invoice is paid. Nothing here
/// ever cancels the subscription: once paid, it carries on with its original billing date.
/// </summary>
public static class BillingRules
{
    /// <summary>Subscription statuses that mean the subscription is still running.</summary>
    public static readonly string[] LiveStatuses = ["active", "trialing", "past_due", "unpaid", "incomplete", "paused"];

    public static bool IsLive(BillingAccount? account) =>
        account?.StripeSubscriptionId is not null && LiveStatuses.Contains(account.SubscriptionStatus);

    /// <summary>
    /// The next renewal date on the clinic's original billing cycle, for subscribing again after a
    /// subscription ended: same day (and time) of the month, or of the year, as it always was.
    /// </summary>
    public static DateTime? NextAnchor(BillingAccount? account, DateTime now)
    {
        if (account?.BillingCycleAnchor is not { } anchor || account.PlanInterval is null) return null;
        var count = Math.Max(account.PlanIntervalCount, 1);
        Func<int, DateTime> step = account.PlanInterval switch
        {
            "year" => n => anchor.AddYears(n * count),
            "month" => n => anchor.AddMonths(n * count),
            "week" => n => anchor.AddDays(7 * n * count),
            "day" => n => anchor.AddDays(n * count),
            _ => _ => anchor,
        };
        // Stepping from the original anchor each time keeps the 31st on the 31st (or the month's end).
        // At least an hour ahead: Stripe needs the anchor in the future when the checkout completes.
        for (var n = 0; n < 10_000; n++)
            if (step(n) > now.AddHours(1)) return step(n);
        return null;
    }

    public static BillingEvaluation Evaluate(BillingAccount? account, BillingConfig options, DateTime now)
    {
        if (!options.Enabled) return new(BillingState.Disabled);

        DateTime? dueSince;
        BillingDueReason reason;
        // An unpaid invoice counts only while its subscription runs: paying an invoice of a
        // cancelled subscription would not restore it, so that case is handled as Ended (subscribe again).
        if (IsLive(account) && account!.UnpaidInvoiceId is not null && account.UnpaidSince is { } unpaidSince)
        {
            dueSince = unpaidSince;
            reason = BillingDueReason.PaymentFailed;
        }
        else if (IsLive(account))
        {
            return new(BillingState.Active);
        }
        else if (account?.StripeSubscriptionId is not null)
        {
            dueSince = account.OverdueSince ?? account.EndedAt ?? account.CurrentPeriodEnd;
            reason = BillingDueReason.Ended;
        }
        else
        {
            dueSince = options.FirstPaymentDue;
            reason = BillingDueReason.NotSubscribed;
        }

        if (dueSince is null || (reason == BillingDueReason.NotSubscribed && now < dueSince))
            return new(BillingState.NotSubscribed, DueSince: dueSince);

        var blockAt = dueSince.Value.AddDays(options.GraceDays);
        if (now < blockAt)
        {
            var daysLeft = (int)Math.Ceiling((blockAt - now).TotalDays);
            return new(BillingState.PaymentDue, reason, dueSince, blockAt, Math.Max(daysLeft, 1));
        }

        // A failed renewal gets one more automatic charge before blocking.
        if (reason == BillingDueReason.PaymentFailed && account!.FinalRetryAt is null)
            return new(BillingState.FinalAttempt, reason, dueSince, blockAt, 0);

        return new(BillingState.Blocked, reason, dueSince, blockAt, 0);
    }
}

/// <summary>
/// The billing row kept in memory, so the access check on every request costs no database or
/// Stripe call. Loaded on first use and replaced whenever BillingService saves a change.
/// </summary>
public class BillingAccessCache
{
    private volatile BillingAccount? _account;
    private volatile bool _loaded;

    public async Task<BillingAccount?> GetAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (_loaded) return _account;
        var account = await db.BillingAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == BillingAccount.SingletonId, ct);
        Set(account);
        return account;
    }

    public void Set(BillingAccount? account)
    {
        _account = account?.Copy();
        _loaded = true;
    }
}
