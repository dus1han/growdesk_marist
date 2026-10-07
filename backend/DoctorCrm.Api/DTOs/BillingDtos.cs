using DoctorCrm.Api.Services;

namespace DoctorCrm.Api.DTOs;

/// <summary>
/// What every signed-in user's screen needs: whether the system is blocked and how many days are
/// left. Amounts are only included for users who can pay.
/// </summary>
public record BillingNoticeDto(
    string State,
    string? Reason,
    bool Blocked,
    int? DaysLeft,
    DateTime? DueSince,
    DateTime? BlockAt,
    long? AmountDue,
    string? Currency,
    bool CanManage);

/// <summary>The admin's Subscription page.</summary>
public record BillingOverviewDto(
    BillingNoticeDto Notice,
    string? SubscriptionStatus,
    bool CancelAtPeriodEnd,
    DateTime? CurrentPeriodEnd,
    PlanInfo? Plan,
    string? CardBrand,
    string? CardLast4,
    DateTime? FinalRetryAt,
    string? FinalRetryError,
    DateTime? LastSyncedAt);

public record BillingRedirectDto(string Url);

/// <summary>Result of Pay now: paid with the card on file, or a Stripe page to finish paying on.</summary>
public record PayNowResultDto(bool Paid, string? RedirectUrl, string? Message);

/// <summary>Administration → Stripe Settings. Secrets are never returned, only whether one is saved.</summary>
public record BillingSettingsDto(
    bool Enabled,
    bool HasSecretKey,
    string? SecretKeyHint,
    string? Mode,
    bool SecretKeyUnreadable,
    bool HasWebhookSecret,
    string? PriceId,
    int GraceDays,
    DateOnly? FirstPaymentDue,
    PlanInfo? Plan,
    string? PlanError,
    string? WebhookUrl,
    IReadOnlyList<string> WebhookEvents,
    DateTime? UpdatedAt);

/// <summary>Leave a secret empty to keep the saved one.</summary>
public record SaveBillingSettingsRequest(
    bool Enabled,
    string? SecretKey,
    string? WebhookSecret,
    bool RemoveWebhookSecret,
    string? PriceId,
    int GraceDays,
    DateOnly? FirstPaymentDue);
