namespace DoctorCrm.Api.DTOs;

public class BookingQuery
{
    /// <summary>Inclusive clinic-local date range (calendar view).</summary>
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public int? CustomerId { get; set; }
    public int? DoctorId { get; set; }
    /// <summary>Comma-separated statuses, e.g. "Booked,Completed".</summary>
    public string? Status { get; set; }
    public int? TreatmentId { get; set; }
    /// <summary>Paid, PartlyPaid, Unpaid, Waived, NoCharge, or Outstanding (anything still owed).</summary>
    public string? PaymentStatus { get; set; }
    /// <summary>Customer name, WhatsApp number or Instagram name.</summary>
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    /// <summary>"asc" (default, upcoming first) or "desc" (history).</summary>
    public string? Sort { get; set; }
}

public record BookingListItemDto(
    int Id,
    NamedRef Customer,
    NamedRef? Doctor,
    DateOnly Date,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string Status,
    IReadOnlyList<NamedRef> Treatments,
    decimal? ConsultationCharge,
    decimal AmountPaid,
    decimal Balance,
    /// <summary>Paid, PartlyPaid, Unpaid, Waived or NoCharge; null until completed.</summary>
    string? PaymentStatus);

public record PaymentDto(
    int Id,
    decimal Amount,
    string Status,
    NamedRef? Method,
    DateTime? PaymentDate,
    string? RecordedBy,
    DateTime CreatedAt);

public record BookingLinkDto(int Id, DateOnly Date, TimeOnly StartTime, string Status);

public record BookingDetailDto(
    int Id,
    NamedRef Customer,
    string? CustomerWhatsApp,
    StageRef CustomerStage,
    NamedRef? Doctor,
    DateOnly Date,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string Status,
    IReadOnlyList<NamedRef> Treatments,
    string? Notes,
    decimal? ConsultationCharge,
    decimal AmountPaid,
    decimal Balance,
    string? PaymentStatus,
    string? DoctorNotes,
    DateOnly? NextTreatmentDate,
    NamedRef? NextTreatment,
    NamedRef? CancellationReason,
    string? CancellationNote,
    BookingLinkDto? RescheduledFrom,
    BookingLinkDto? RescheduledTo,
    IReadOnlyList<PaymentDto> Payments,
    DateTime? CompletedAt,
    DateTime? CancelledAt,
    DateTime? RescheduledAt,
    DateTime? NoShowAt,
    DateTime CreatedAt,
    /// <summary>"WhatsApp BOT" when the bot made the booking; null when made in GrowDesk.</summary>
    string? Source);

public record CreateBookingRequest(
    int CustomerId,
    int? DoctorId,
    DateOnly Date,
    TimeOnly StartTime,
    TimeOnly EndTime,
    IReadOnlyList<int> TreatmentIds,
    string? Notes);

/// <summary>Edits a booked consultation's treatments, doctor and notes. Time changes go through reschedule.</summary>
public record UpdateBookingRequest(int? DoctorId, IReadOnlyList<int> TreatmentIds, string? Notes);

/// <summary>
/// Completes a consultation. PaidAmount is what the customer paid now (0 up to the charge); the
/// rest stays as the consultation's balance, to be paid later or waived.
/// </summary>
public record CompleteBookingRequest(
    decimal ConsultationCharge,
    decimal PaidAmount,
    int? PaymentMethodId,
    DateOnly? NextTreatmentDate,
    int? NextTreatmentId,
    string? DoctorNotes,
    /// <summary>Required when the booking has no treatment yet (the WhatsApp BOT couldn't tell): what was done.</summary>
    IReadOnlyList<int>? TreatmentIds = null);

public record RescheduleBookingRequest(DateOnly Date, TimeOnly StartTime, TimeOnly EndTime, int? DoctorId);

public record CancelBookingRequest(int CancellationReasonId, string? Note);

/// <summary>Returned with a 409 when the chosen time overlaps another booked consultation.</summary>
public record BookingConflictDto(int BookingId, string CustomerName, TimeOnly StartTime, TimeOnly EndTime);

public record LocaleDto(string Currency, string TimeZone, DateOnly Today);
