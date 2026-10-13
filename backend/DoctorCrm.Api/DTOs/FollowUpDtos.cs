namespace DoctorCrm.Api.DTOs;

/// <summary>Administration → Follow-ups. Reminder times are clinic-local "HH:mm".</summary>
public record FollowUpSettingsDto(int IntervalDays, int MaxAttempts, IReadOnlyList<string> ReminderTimes);

/// <summary>A customer due a follow-up.</summary>
public record FollowUpItemDto(
    int Id,
    string Name,
    string? WhatsApp,
    string? Instagram,
    StageRef Stage,
    IReadOnlyList<string> Treatments,
    string? LeadSource,
    NamedRef? AssignedUser,
    /// <summary>Follow-up date: before today means overdue.</summary>
    DateOnly Due,
    /// <summary>Follow-ups logged since the cycle last started (new enquiry, cancellation, no-show).</summary>
    int Attempts,
    DateOnly? LastContactDate,
    DateTime CreatedAt,
    /// <summary>A new enquiry nobody has contacted yet.</summary>
    bool IsNew);

public record FollowUpQueueDto(DateOnly Today, int MaxAttempts, IReadOnlyList<FollowUpItemDto> Items, IReadOnlyList<NextTreatmentItemDto> NextTreatments);

/// <summary>
/// A completed consultation whose next treatment date was set but never booked (and the customer
/// has nothing else booked). BookingId is the completed consultation; book with NextTreatmentOf = BookingId.
/// </summary>
public record NextTreatmentItemDto(
    int BookingId,
    int CustomerId,
    string CustomerName,
    string? WhatsApp,
    DateOnly Date,
    NamedRef? Treatment,
    NamedRef? Doctor);

public record NewEnquiryDto(int Id, string Name, DateTime CreatedAt, string? LeadSource, string? Treatment);

/// <summary>For the sidebar badge and reminders: the signed-in user's own and unassigned customers.</summary>
public record FollowUpSummaryDto(
    DateOnly Today,
    int Due,
    int Overdue,
    int New,
    IReadOnlyList<NewEnquiryDto> NewEnquiries,
    IReadOnlyList<string> ReminderTimes);

/// <summary>
/// Outcome: CallBack, NoAnswer or NotInterested. NextFollowUpDate: when to follow up again
/// (default: in the configured number of days); ignored for NotInterested.
/// </summary>
public record LogFollowUpRequest(string Outcome, string? Note, DateOnly? NextFollowUpDate);

/// <summary>SuggestLost: the attempt limit is reached; offer to mark the customer Lost.</summary>
public record FollowUpLoggedDto(DateOnly? NextFollowUpDate, int Attempts, bool SuggestLost, string Stage);
