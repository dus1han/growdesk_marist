namespace DoctorCrm.Api.Entities;

/// <summary>
/// This clinic's subscription to the GrowDesk platform: a single row (Id 1) holding the Stripe
/// customer and the last state synced from Stripe. Stripe is the source of truth; this row is a
/// snapshot so every request can check access without calling Stripe.
/// </summary>
public class BillingAccount
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public string? StripeCustomerId { get; set; }
    public string? StripeSubscriptionId { get; set; }

    /// <summary>Stripe's subscription status: active, trialing, past_due, unpaid, canceled, …</summary>
    public string? SubscriptionStatus { get; set; }
    public bool CancelAtPeriodEnd { get; set; }
    public DateTime? CurrentPeriodEnd { get; set; }
    public DateTime? EndedAt { get; set; }

    /// <summary>The date the billing cycle is anchored to (renewals fall on it). Kept after the
    /// subscription ends, so subscribing again keeps the original billing date.</summary>
    public DateTime? BillingCycleAnchor { get; set; }

    /// <summary>Plan price in the currency's minor unit (e.g. fils, cents).</summary>
    public long? PlanAmount { get; set; }
    public string? PlanCurrency { get; set; }
    public string? PlanInterval { get; set; }
    public int PlanIntervalCount { get; set; } = 1;

    public string? CardBrand { get; set; }
    public string? CardLast4 { get; set; }

    /// <summary>The oldest subscription invoice Stripe tried to charge and could not.</summary>
    public string? UnpaidInvoiceId { get; set; }
    public long? UnpaidAmount { get; set; }
    public string? UnpaidCurrency { get; set; }

    /// <summary>When that invoice fell due; the grace period runs from here.</summary>
    public DateTime? UnpaidSince { get; set; }
    public string? UnpaidInvoiceUrl { get; set; }

    /// <summary>
    /// When the clinic first fell behind, remembered until the subscription is running and paid
    /// again. If Stripe cancels the subscription (or voids the invoice) while it is unpaid, the
    /// countdown keeps running from here instead of starting over.
    /// </summary>
    public DateTime? OverdueSince { get; set; }

    /// <summary>
    /// When the last automatic charge was tried at the end of the grace period, for the current
    /// unpaid invoice. If it failed the system is blocked until the invoice is paid.
    /// </summary>
    public DateTime? FinalRetryAt { get; set; }
    public string? FinalRetryError { get; set; }

    public DateTime? LastSyncedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>A detached copy for the in-memory access cache.</summary>
    public BillingAccount Copy() => (BillingAccount)MemberwiseClone();
}
