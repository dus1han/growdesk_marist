using DoctorCrm.Api.Data;
using DoctorCrm.Api.DTOs;
using DoctorCrm.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace DoctorCrm.Api.Services;

/// <summary>
/// The clinic's GrowDesk subscription. Stripe charges the card automatically each period; this
/// service mirrors Stripe's state into the billing row, runs the grace-period rules
/// (<see cref="BillingRules"/>), makes the last automatic charge when the grace period ends, and
/// lets an admin pay by hand.
/// </summary>
public class BillingService(
    AppDbContext db,
    IBillingGateway gateway,
    BillingAccessCache cache,
    BillingConfigStore config,
    AuditService audit,
    TimeProvider time,
    ILogger<BillingService> logger)
{
    private const string EntityType = "Subscription";

    // One sync or charge at a time, so the background job and a Pay now click never race.
    private static readonly SemaphoreSlim Lock = new(1, 1);

    private DateTime Now => time.GetUtcNow().UtcDateTime;

    private Task<BillingConfig> ConfigAsync(CancellationToken ct) => config.GetAsync(db, ct);

    public async Task<BillingEvaluation> EvaluateAsync(CancellationToken ct) =>
        BillingRules.Evaluate(await cache.GetAsync(db, ct), await ConfigAsync(ct), Now);

    public async Task<BillingNoticeDto> GetNoticeAsync(bool canManage, CancellationToken ct)
    {
        var account = await cache.GetAsync(db, ct);
        return ToNotice(BillingRules.Evaluate(account, await ConfigAsync(ct), Now), account, canManage);
    }

    public async Task<BillingOverviewDto> GetOverviewAsync(CancellationToken ct)
    {
        var cfg = await ConfigAsync(ct);
        var account = await cache.GetAsync(db, ct);
        var notice = ToNotice(BillingRules.Evaluate(account, cfg, Now), account, canManage: true);

        // Before the first subscription, show the plan the clinic is about to subscribe to.
        var plan = account?.PlanAmount is { } amount
            ? new PlanInfo(null, amount, account.PlanCurrency!, account.PlanInterval ?? "month", account.PlanIntervalCount)
            : cfg.Enabled ? await TryGetConfiguredPlanAsync(cfg, ct) : null;

        return new BillingOverviewDto(
            notice,
            account?.SubscriptionStatus,
            account?.CancelAtPeriodEnd ?? false,
            account?.CurrentPeriodEnd,
            plan,
            account?.CardBrand,
            account?.CardLast4,
            account?.FinalRetryAt,
            account?.FinalRetryError,
            account?.LastSyncedAt);
    }

    public async Task<IReadOnlyList<BillingInvoice>> ListInvoicesAsync(CancellationToken ct)
    {
        await EnsureEnabledAsync(ct);
        var account = await cache.GetAsync(db, ct);
        return account?.StripeCustomerId is null ? [] : await gateway.ListInvoicesAsync(account.StripeCustomerId, ct);
    }

    /// <summary>Starts a subscription: a Stripe Checkout page where the card is entered and saved.</summary>
    public async Task<BillingRedirectDto> StartSubscriptionAsync(int userId, string? origin, CancellationToken ct)
    {
        await EnsureEnabledAsync(ct);
        var account = await LoadAsync(ct);
        if (BillingRules.IsLive(account))
            throw BusinessRuleException.Conflict("The subscription is already active.");

        return new BillingRedirectDto(await CheckoutAsync(account, userId, origin, ct));
    }

    /// <summary>
    /// Pay now: charges the card on file for the overdue invoice. If that card is declined, the
    /// admin is sent to Stripe's invoice page to pay with another card. Without a running
    /// subscription there is no invoice to pay, so it subscribes again (on the original billing date).
    /// </summary>
    public async Task<PayNowResultDto> PayNowAsync(int userId, string? origin, CancellationToken ct)
    {
        await EnsureEnabledAsync(ct);
        await SyncAsync(ct);
        var account = await LoadAsync(ct);

        if (BillingRules.IsLive(account) && account.UnpaidInvoiceId is { } invoiceId)
        {
            var result = await gateway.PayInvoiceAsync(invoiceId, ct);
            if (result.Paid)
            {
                audit.Record(userId, "Subscription Paid", EntityType, invoiceId, new { amount = account.UnpaidAmount, currency = account.UnpaidCurrency, by = "Pay now" });
                await db.SaveChangesAsync(ct);
                await SyncAsync(ct);
                return new PayNowResultDto(true, null, "Payment received. Thank you!");
            }

            audit.Record(userId, "Subscription Payment Failed", EntityType, invoiceId, new { error = result.Error, by = "Pay now" });
            await db.SaveChangesAsync(ct);
            return new PayNowResultDto(false, account.UnpaidInvoiceUrl,
                $"{result.Error ?? "The card on file could not be charged."} You can pay with another card on the next page.");
        }

        if (BillingRules.IsLive(account))
            return new PayNowResultDto(true, null, "There is nothing to pay. Your subscription is up to date.");

        return new PayNowResultDto(false, await CheckoutAsync(account, userId, origin, ct), null);
    }

    /// <summary>Stripe's billing portal, to change the card that renewals are charged to.</summary>
    public async Task<BillingRedirectDto> PortalAsync(string? origin, CancellationToken ct)
    {
        var cfg = await EnsureEnabledAsync(ct);
        var account = await LoadAsync(ct);
        if (account.StripeCustomerId is null)
            throw new BusinessRuleException("Subscribe first to save a card.");
        return new BillingRedirectDto(await gateway.CreatePortalAsync(account.StripeCustomerId, $"{ReturnBase(cfg, origin)}/administration/subscription", ct));
    }

    /// <summary>A Stripe webhook arrived: verify it and, if it is about this clinic, refresh from Stripe.</summary>
    public async Task HandleWebhookAsync(string json, string signature, CancellationToken ct)
    {
        var cfg = await ConfigAsync(ct);
        if (!cfg.Enabled || string.IsNullOrWhiteSpace(cfg.WebhookSecret))
            throw BusinessRuleException.NotFound("Webhook");

        WebhookEvent evt;
        try
        {
            evt = gateway.ParseWebhook(json, signature, cfg.WebhookSecret);
        }
        catch (BillingWebhookException ex)
        {
            logger.LogWarning("Rejected Stripe webhook: {Reason}", ex.Message);
            throw new BusinessRuleException("Invalid signature.");
        }

        // One Stripe account can bill several clinics; each ignores the others' events.
        var account = await cache.GetAsync(db, ct);
        if (evt.CustomerId is null || evt.CustomerId != account?.StripeCustomerId) return;

        logger.LogInformation("Stripe webhook {Type}: refreshing the subscription", evt.Type);
        await SyncAsync(ct);
    }

    /// <summary>
    /// The background job: refreshes from Stripe every <see cref="BillingOptions.SyncMinutes"/>
    /// minutes and, once the grace period has run out, charges the card one last time.
    /// </summary>
    public async Task RunScheduledChecksAsync(CancellationToken ct)
    {
        var cfg = await ConfigAsync(ct);
        if (!cfg.Enabled) return;

        var account = await cache.GetAsync(db, ct);
        if (account?.StripeCustomerId is not null &&
            (account.LastSyncedAt is null || account.LastSyncedAt < Now.AddMinutes(-cfg.Server.SyncMinutes)))
            await SyncAsync(ct);

        if ((await EvaluateAsync(ct)).State == BillingState.FinalAttempt)
            await FinalAttemptAsync(ct);
    }

    /// <summary>
    /// The grace period is over: charge the card on file one last time. If that fails the system
    /// stays blocked; the subscription is left running (past due) so that, once paid, it continues
    /// on its original billing date.
    /// </summary>
    private async Task FinalAttemptAsync(CancellationToken ct)
    {
        // Stripe may have collected the payment in the meantime (its own retries, or a payment
        // made on the invoice page): check before charging again.
        await SyncAsync(ct);
        var account = await LoadAsync(ct);
        var cfg = await ConfigAsync(ct);
        if (BillingRules.Evaluate(account, cfg, Now).State != BillingState.FinalAttempt) return;

        var invoiceId = account.UnpaidInvoiceId!;
        logger.LogInformation("Grace period over: last automatic charge for invoice {InvoiceId}", invoiceId);
        var result = await gateway.PayInvoiceAsync(invoiceId, ct);

        await Lock.WaitAsync(ct);
        try
        {
            account.FinalRetryAt = Now;
            account.FinalRetryError = result.Paid ? null : result.Error ?? "The card on file could not be charged.";
            account.UpdatedAt = Now;
            if (result.Paid)
                audit.Record(null, "Subscription Paid", EntityType, invoiceId, new { amount = account.UnpaidAmount, currency = account.UnpaidCurrency, by = "Automatic retry" });
            else
                audit.Record(null, "Subscription Blocked", EntityType, invoiceId, new { error = account.FinalRetryError });
            await db.SaveChangesAsync(ct);
            cache.Set(account);
        }
        finally
        {
            Lock.Release();
        }

        if (result.Paid) await SyncAsync(ct);
        else logger.LogWarning("Last automatic charge failed ({Error}): the system is blocked until the invoice is paid", account.FinalRetryError);
    }

    /// <summary>Copies the subscription, card and oldest unpaid invoice from Stripe into the billing row.</summary>
    public async Task SyncAsync(CancellationToken ct)
    {
        if (!(await ConfigAsync(ct)).Enabled) return;

        await Lock.WaitAsync(ct);
        try
        {
            var account = await LoadAsync(ct);
            if (account.StripeCustomerId is null)
            {
                cache.Set(account.Id == 0 ? null : account);
                return;
            }

            var snapshot = await gateway.FetchAsync(account.StripeCustomerId, ct);
            var hadUnpaid = account.UnpaidInvoiceId;

            account.StripeSubscriptionId = snapshot.SubscriptionId;
            account.SubscriptionStatus = snapshot.Status;
            account.CancelAtPeriodEnd = snapshot.CancelAtPeriodEnd;
            account.CurrentPeriodEnd = snapshot.CurrentPeriodEnd;
            account.EndedAt = snapshot.EndedAt;
            account.BillingCycleAnchor = snapshot.BillingCycleAnchor ?? account.BillingCycleAnchor;
            if (snapshot.Plan is { } plan)
            {
                account.PlanAmount = plan.Amount;
                account.PlanCurrency = plan.Currency;
                account.PlanInterval = plan.Interval;
                account.PlanIntervalCount = plan.IntervalCount;
            }
            account.CardBrand = snapshot.CardBrand;
            account.CardLast4 = snapshot.CardLast4;

            account.UnpaidInvoiceId = snapshot.Unpaid?.Id;
            account.UnpaidAmount = snapshot.Unpaid?.Amount;
            account.UnpaidCurrency = snapshot.Unpaid?.Currency;
            account.UnpaidSince = snapshot.Unpaid?.DueSince;
            account.UnpaidInvoiceUrl = snapshot.Unpaid?.HostedUrl;

            // Behind since the earliest missed payment, until the subscription runs and is paid up.
            if (account.UnpaidSince is { } since)
                account.OverdueSince = account.OverdueSince is { } earlier && earlier < since ? earlier : since;
            else if (BillingRules.IsLive(account))
                account.OverdueSince = null;

            // The last-charge record belongs to one invoice; a new or settled invoice starts afresh.
            if (account.UnpaidInvoiceId != hadUnpaid)
            {
                account.FinalRetryAt = null;
                account.FinalRetryError = null;
                if (hadUnpaid is not null)
                    audit.Record(null, "Subscription Payment Settled", EntityType, hadUnpaid);
                if (account.UnpaidInvoiceId is not null)
                    audit.Record(null, "Subscription Payment Failed", EntityType, account.UnpaidInvoiceId,
                        new { amount = account.UnpaidAmount, currency = account.UnpaidCurrency });
            }

            account.LastSyncedAt = Now;
            account.UpdatedAt = Now;
            await db.SaveChangesAsync(ct);
            cache.Set(account);
        }
        finally
        {
            Lock.Release();
        }
    }

    private async Task<string> CheckoutAsync(BillingAccount account, int userId, string? origin, CancellationToken ct)
    {
        var cfg = await ConfigAsync(ct);
        var returnBase = ReturnBase(cfg, origin);
        if (account.StripeCustomerId is null)
        {
            var name = await db.SystemSettings.AsNoTracking()
                .Where(s => s.Key == SettingKeys.CrmName).Select(s => s.Value).SingleOrDefaultAsync(ct) ?? "GrowDesk";
            var email = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Email).SingleOrDefaultAsync(ct);
            account.StripeCustomerId = await gateway.CreateCustomerAsync(name, email, ct);
            account.UpdatedAt = Now;
            if (account.Id == 0)
            {
                account.Id = BillingAccount.SingletonId;
                db.BillingAccounts.Add(account);
            }
        }

        // Subscribing again after a subscription ended keeps the clinic's original billing date.
        var anchor = BillingRules.NextAnchor(account, Now);
        var url = await gateway.CreateSubscriptionCheckoutAsync(
            account.StripeCustomerId, cfg.PriceId, anchor,
            $"{returnBase}/administration/subscription?checkout=success",
            $"{returnBase}/administration/subscription?checkout=cancelled", ct);

        audit.Record(userId, "Subscription Checkout Started", EntityType, account.StripeCustomerId, new { renewsOn = anchor });
        await db.SaveChangesAsync(ct);
        cache.Set(account);
        return url;
    }

    /// <summary>The tracked billing row, or a new unsaved one (Id 0) if the clinic has none yet.</summary>
    private async Task<BillingAccount> LoadAsync(CancellationToken ct) =>
        await db.BillingAccounts.SingleOrDefaultAsync(a => a.Id == BillingAccount.SingletonId, ct)
        ?? new BillingAccount { Id = 0 };

    private async Task<PlanInfo?> TryGetConfiguredPlanAsync(BillingConfig cfg, CancellationToken ct)
    {
        try
        {
            return await gateway.GetPlanAsync(cfg.PriceId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not read the plan price {PriceId} from Stripe", cfg.PriceId);
            return null;
        }
    }

    /// <summary>Where Stripe sends the admin back to: Billing:AppUrl, else the browser's own origin.</summary>
    public static string ReturnBase(BillingConfig cfg, string? origin)
    {
        if (!string.IsNullOrWhiteSpace(cfg.Server.AppUrl)) return cfg.Server.AppUrl.TrimEnd('/');
        if (Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            return uri.GetLeftPart(UriPartial.Authority);
        throw new BusinessRuleException("Billing:AppUrl is not configured.");
    }

    private async Task<BillingConfig> EnsureEnabledAsync(CancellationToken ct)
    {
        var cfg = await ConfigAsync(ct);
        if (!cfg.Enabled)
            throw new BusinessRuleException("Online subscription payments are not set up on this server.");
        return cfg;
    }

    private static BillingNoticeDto ToNotice(BillingEvaluation e, BillingAccount? account, bool canManage)
    {
        var amount = (BillingRules.IsLive(account) ? account!.UnpaidAmount : null)
            ?? (e.State is BillingState.PaymentDue or BillingState.Blocked ? account?.PlanAmount : null);
        var currency = (BillingRules.IsLive(account) ? account!.UnpaidCurrency : null) ?? account?.PlanCurrency;
        return new BillingNoticeDto(
            e.State.ToString(),
            e.Reason?.ToString(),
            e.IsBlocked,
            e.DaysLeft,
            e.DueSince,
            e.BlockAt,
            canManage ? amount : null,
            canManage && amount is not null ? currency : null,
            canManage);
    }
}
