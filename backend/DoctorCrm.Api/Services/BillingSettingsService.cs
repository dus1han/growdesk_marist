using DoctorCrm.Api.Data;
using DoctorCrm.Api.DTOs;
using DoctorCrm.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace DoctorCrm.Api.Services;

/// <summary>
/// Administration → Stripe Settings (platform owners only): the Stripe keys, the plan's price and
/// the grace period. Keys are checked with Stripe before they are saved and are stored encrypted.
/// </summary>
public class BillingSettingsService(
    AppDbContext db,
    IBillingGateway gateway,
    BillingConfigStore config,
    BillingAccessCache accessCache,
    SecretProtector protector,
    ClinicClock clock,
    AuditService audit)
{
    /// <summary>The webhook events GrowDesk listens to (any one of them triggers a refresh).</summary>
    public static readonly string[] WebhookEvents =
    [
        "checkout.session.completed",
        "customer.subscription.created",
        "customer.subscription.updated",
        "customer.subscription.deleted",
        "invoice.paid",
        "invoice.payment_failed",
        "invoice.finalized",
        "invoice.voided",
        "invoice.marked_uncollectible",
    ];

    public async Task<BillingSettingsDto> GetAsync(string? origin, CancellationToken ct)
    {
        var row = await db.BillingSettings.AsNoTracking().SingleOrDefaultAsync(s => s.Id == BillingSettings.SingletonId, ct);
        var key = protector.Unprotect(row?.SecretKeyProtected);

        PlanInfo? plan = null;
        string? planError = null;
        if (key is not null && !string.IsNullOrWhiteSpace(row?.PriceId))
        {
            try
            {
                plan = await gateway.ValidateAsync(key, row.PriceId, ct);
            }
            catch (BillingConfigException ex)
            {
                planError = ex.Message;
            }
        }

        string? webhookUrl;
        try
        {
            webhookUrl = $"{BillingService.ReturnBase(await config.GetAsync(db, ct), origin)}/api/billing/webhook";
        }
        catch (BusinessRuleException)
        {
            webhookUrl = null;
        }

        return new BillingSettingsDto(
            row?.Enabled ?? false,
            key is not null,
            row?.SecretKeyHint,
            ModeOf(key),
            row?.SecretKeyProtected is not null && key is null,
            row?.WebhookSecretProtected is not null,
            row?.PriceId,
            row?.GraceDays ?? 3,
            row?.FirstPaymentDue is { } due ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(due, await clock.ZoneAsync(ct))) : null,
            plan,
            planError,
            webhookUrl,
            WebhookEvents,
            row?.UpdatedAt);
    }

    public async Task<BillingSettingsDto> SaveAsync(SaveBillingSettingsRequest request, int actorId, string? origin, CancellationToken ct)
    {
        var row = await db.BillingSettings.SingleOrDefaultAsync(s => s.Id == BillingSettings.SingletonId, ct);
        if (row is null)
        {
            row = new BillingSettings();
            db.BillingSettings.Add(row);
        }

        var currentKey = protector.Unprotect(row.SecretKeyProtected);
        var enteredKey = string.IsNullOrWhiteSpace(request.SecretKey) ? null : request.SecretKey.Trim();
        var key = enteredKey ?? currentKey;
        var priceId = string.IsNullOrWhiteSpace(request.PriceId) ? null : request.PriceId.Trim();

        if (request.Enabled && key is null)
            throw new BusinessRuleException("Enter your Stripe secret key to switch billing on.", field: "secretKey");
        if (request.Enabled && priceId is null)
            throw new BusinessRuleException("Enter the price ID of your plan to switch billing on.", field: "priceId");

        // Never save a key or price that doesn't work: a typo here would switch billing off silently.
        if (key is not null && priceId is not null && (enteredKey is not null || priceId != row.PriceId || request.Enabled))
        {
            try
            {
                await gateway.ValidateAsync(key, priceId, ct);
            }
            catch (BillingConfigException ex)
            {
                throw new BusinessRuleException(ex.Message, field: ex.Field);
            }
        }

        // Customers and subscriptions live in one mode (test or live) of one Stripe account. Moving
        // between test and live starts the clinic's billing afresh in the new mode.
        var modeChanged = currentKey is not null && enteredKey is not null && ModeOf(currentKey) != ModeOf(enteredKey);
        if (modeChanged)
        {
            await db.BillingAccounts.Where(a => a.Id == BillingAccount.SingletonId).ExecuteDeleteAsync(ct);
            accessCache.Set(null);
        }

        if (enteredKey is not null)
        {
            row.SecretKeyProtected = protector.Protect(enteredKey);
            row.SecretKeyHint = Hint(enteredKey);
        }

        var webhookChanged = request.RemoveWebhookSecret || !string.IsNullOrWhiteSpace(request.WebhookSecret);
        if (request.RemoveWebhookSecret) row.WebhookSecretProtected = null;
        else if (!string.IsNullOrWhiteSpace(request.WebhookSecret)) row.WebhookSecretProtected = protector.Protect(request.WebhookSecret.Trim());

        row.Enabled = request.Enabled;
        row.PriceId = priceId;
        row.GraceDays = request.GraceDays;
        row.FirstPaymentDue = request.FirstPaymentDue is { } due ? await clock.StartOfDayUtcAsync(due, ct) : null;
        row.UpdatedAt = DateTime.UtcNow;
        row.UpdatedById = actorId;

        audit.Record(actorId, "Stripe Settings Updated", "Subscription", null, new
        {
            enabled = row.Enabled,
            mode = ModeOf(key),
            priceId,
            graceDays = row.GraceDays,
            firstPaymentDue = request.FirstPaymentDue,
            secretKeyChanged = enteredKey is not null,
            webhookSecretChanged = webhookChanged,
            billingRestarted = modeChanged,
        });
        await db.SaveChangesAsync(ct);
        config.Set(row);

        return await GetAsync(origin, ct);
    }

    /// <summary>"live" or "test", from the key's prefix (sk_live_…, rk_test_…).</summary>
    private static string? ModeOf(string? key) =>
        key is null ? null : key.Contains("_live_", StringComparison.Ordinal) ? "live" : "test";

    private static string Hint(string key)
    {
        if (key.Length <= 12) return "••••";
        var prefixEnd = key.IndexOf('_', 3);
        return $"{key[..(prefixEnd > 0 ? prefixEnd + 1 : 3)]}…{key[^4..]}";
    }
}
