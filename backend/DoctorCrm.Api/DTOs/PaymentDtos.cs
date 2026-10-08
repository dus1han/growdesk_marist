namespace DoctorCrm.Api.DTOs;

/// <summary>Query string for GET /api/payments (and its summary and export). Filters combine.</summary>
public record PaymentQuery
{
    /// <summary>Inclusive clinic-local dates on which the payment was recorded.</summary>
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    /// <summary>Paid or Waived.</summary>
    public string? Status { get; set; }
    public int? PaymentMethodId { get; set; }
    public int? CustomerId { get; set; }
    /// <summary>Customer name, WhatsApp number or Instagram name.</summary>
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public record PaymentBookingRef(int Id, DateOnly Date, TimeOnly StartTime, IReadOnlyList<string> Treatments);

/// <summary>One payment entry: money received (Paid) or a balance written off (Waived).</summary>
public record PaymentListItemDto(
    int Id,
    NamedRef Customer,
    PaymentBookingRef Booking,
    decimal Amount,
    string Status,
    NamedRef? Method,
    DateTime? PaymentDate,
    DateTime CreatedAt,
    string? RecordedBy,
    /// <summary>What the consultation still owes now, after every entry.</summary>
    decimal BookingBalance);

/// <summary>
/// Totals for the filtered range. Collected and waived count entries recorded in the range;
/// outstanding is every balance still owed now, whatever the range, because it is money owed today.
/// </summary>
public record PaymentSummaryDto(
    decimal Collected,
    int CollectedCount,
    decimal Outstanding,
    int OutstandingCount,
    decimal Waived,
    int WaivedCount,
    string Currency);

/// <summary>Query string for GET /api/payments/outstanding: consultations with a balance.</summary>
public record OutstandingQuery
{
    public int? CustomerId { get; set; }
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

/// <summary>A completed consultation that still has a balance.</summary>
public record OutstandingItemDto(
    int BookingId,
    NamedRef Customer,
    DateOnly Date,
    TimeOnly StartTime,
    IReadOnlyList<string> Treatments,
    decimal Charge,
    decimal Paid,
    decimal Balance);

/// <summary>Money received for a consultation: any amount up to its balance (part payments allowed).</summary>
public record RecordPaymentRequest(decimal Amount, int PaymentMethodId, DateOnly? PaymentDate);
