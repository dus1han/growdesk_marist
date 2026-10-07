namespace DoctorCrm.Api.Entities;

/// <summary>
/// Stripe Settings, edited by a platform owner in Administration: a single row (Id 1). Secrets are
/// stored encrypted (<see cref="Services.SecretProtector"/>) and never sent back to the browser.
/// </summary>
public class BillingSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    /// <summary>Master switch: off means no reminders and no blocking.</summary>
    public bool Enabled { get; set; }

    public string? SecretKeyProtected { get; set; }

    /// <summary>Recognisable, harmless part of the key for the settings screen ("sk_live_…a1B2").</summary>
    public string? SecretKeyHint { get; set; }

    public string? WebhookSecretProtected { get; set; }

    /// <summary>The recurring Stripe price this clinic subscribes to (price_…).</summary>
    public string? PriceId { get; set; }

    /// <summary>Days after a missed payment before the system is blocked.</summary>
    public int GraceDays { get; set; } = 3;

    /// <summary>
    /// For a clinic that has never subscribed: when payment is first due (start of that day in the
    /// clinic's time zone). Unset means an unsubscribed clinic is reminded but never blocked.
    /// </summary>
    public DateTime? FirstPaymentDue { get; set; }

    public DateTime UpdatedAt { get; set; }
    public int? UpdatedById { get; set; }
}
