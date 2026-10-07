using DoctorCrm.Api.Data;
using DoctorCrm.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DoctorCrm.Api.Services;

/// <summary>
/// Server-side billing settings (section "Billing"): only operational values. The Stripe keys,
/// plan and grace period are set by a platform owner in Administration → Stripe Settings.
/// </summary>
public class BillingOptions
{
    public const string Section = "Billing";

    /// <summary>Public address of the app (e.g. https://crm.example.com) for Stripe's return links
    /// and the webhook URL shown in Stripe Settings. Unset: taken from the browser's address.</summary>
    public string AppUrl { get; set; } = "";

    /// <summary>How often the background job refreshes the state from Stripe.</summary>
    public int SyncMinutes { get; set; } = 30;

    /// <summary>How often the background job checks whether the grace period has run out.</summary>
    public int CheckMinutes { get; set; } = 5;

    /// <summary>Turns the background job off (tests drive it directly).</summary>
    public bool WorkerEnabled { get; set; } = true;
}

/// <summary>The billing configuration in force: the saved Stripe Settings plus the server options.</summary>
public record BillingConfig(
    string SecretKey,
    string WebhookSecret,
    string PriceId,
    int GraceDays,
    DateTime? FirstPaymentDue,
    BillingOptions Server)
{
    public static BillingConfig Off(BillingOptions server) => new("", "", "", 3, null, server);

    /// <summary>Billing runs (and can block) only when switched on with a key and a price.</summary>
    public bool Enabled => !string.IsNullOrWhiteSpace(SecretKey) && !string.IsNullOrWhiteSpace(PriceId);
}

/// <summary>
/// The billing configuration kept in memory, so the per-request access check reads no database.
/// Loaded from the Stripe Settings row on first use and replaced whenever an owner saves it.
/// </summary>
public class BillingConfigStore(IOptions<BillingOptions> server, SecretProtector protector, ILogger<BillingConfigStore> logger)
{
    private volatile BillingConfig? _current;

    /// <summary>The configuration once loaded (the gateway runs only after BillingService loaded it).</summary>
    public BillingConfig Current => _current ?? BillingConfig.Off(server.Value);

    public async Task<BillingConfig> GetAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (_current is not null) return _current;
        var settings = await db.BillingSettings.AsNoTracking().SingleOrDefaultAsync(s => s.Id == BillingSettings.SingletonId, ct);
        Set(settings);
        return _current!;
    }

    public void Set(BillingSettings? settings)
    {
        if (settings is null || !settings.Enabled)
        {
            _current = BillingConfig.Off(server.Value) with
            {
                GraceDays = settings?.GraceDays ?? 3,
                PriceId = settings?.PriceId ?? "",
            };
            return;
        }

        var key = protector.Unprotect(settings.SecretKeyProtected);
        if (settings.SecretKeyProtected is not null && key is null)
            logger.LogError("The saved Stripe secret key can't be decrypted (was Jwt:Key changed?). Billing is off until it is entered again in Stripe Settings.");

        _current = new BillingConfig(
            key ?? "",
            protector.Unprotect(settings.WebhookSecretProtected) ?? "",
            settings.PriceId ?? "",
            settings.GraceDays,
            settings.FirstPaymentDue,
            server.Value);
    }
}
