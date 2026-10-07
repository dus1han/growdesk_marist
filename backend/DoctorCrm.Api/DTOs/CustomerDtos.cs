using System.Text.Json;

namespace DoctorCrm.Api.DTOs;

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public record NamedRef(int Id, string Name);

public record StageRef(int Id, string Name, string Color, string? SystemKey);

/// <summary>
/// Where the customer is with consultations, worked out from their bookings every time (never
/// stored, so it can't go stale or be set wrongly). <c>State</c>: "booked" (a consultation is
/// booked: the earliest one), "rescheduled" (the same, but that booking replaced a rescheduled
/// one), "consulted" (the last one was completed), "missed" (the last one was a no-show),
/// "cancelled" (the last one was cancelled), or "none" (never booked). The booking fields
/// describe that consultation; <c>NextTreatmentDate</c> is set on a completed one.
/// </summary>
public record ConsultationDto(string State, int? BookingId, DateOnly? Date, TimeOnly? StartTime, DateOnly? NextTreatmentDate);

public static class ConsultationStates
{
    public const string Booked = "booked";
    public const string Rescheduled = "rescheduled";
    public const string Consulted = "consulted";
    public const string Missed = "missed";
    public const string Cancelled = "cancelled";
    public const string None = "none";
}

/// <summary>Query string for GET /api/customers. Every filter is optional and they combine (AND).</summary>
public class CustomerQuery
{
    public string? Search { get; set; }
    public int? StageId { get; set; }
    /// <summary>booked, rescheduled, consulted, missed, cancelled or none (see ConsultationDto).</summary>
    public string? Consultation { get; set; }
    public int? TreatmentId { get; set; }
    public int? LeadSourceId { get; set; }
    public int? AssignedUserId { get; set; }
    public DateOnly? CreatedFrom { get; set; }
    public DateOnly? CreatedTo { get; set; }
    public DateOnly? FollowUpFrom { get; set; }
    public DateOnly? FollowUpTo { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public record CustomerListItemDto(
    int Id,
    string Name,
    string? WhatsApp,
    string? Instagram,
    StageRef Stage,
    ConsultationDto Consultation,
    IReadOnlyList<NamedRef> Treatments,
    string? LeadSource,
    string? AssignedUser,
    DateOnly? NextFollowUpDate,
    NextBookingDto? NextBooking,
    DateTime CreatedAt);

public record NextBookingDto(int Id, DateOnly Date, TimeOnly StartTime);

/// <summary>
/// A custom field value, typed by field: string (text, long text, phone, email, date as yyyy-MM-dd),
/// number, boolean, option id (dropdown) or array of option ids (multi-select).
/// </summary>
public record CustomFieldValueDto(int FieldId, string Key, string Label, string FieldType, JsonElement Value, string Display);

public record CustomerDetailDto(
    int Id,
    string Name,
    string? WhatsApp,
    string? SecondaryPhone,
    string? Instagram,
    string? Email,
    StageRef Stage,
    ConsultationDto Consultation,
    NamedRef? LeadSource,
    NamedRef? AssignedUser,
    DateOnly? LastContactDate,
    DateOnly? NextFollowUpDate,
    string? Notes,
    bool IsActive,
    IReadOnlyList<NamedRef> Treatments,
    IReadOnlyList<CustomFieldValueDto> CustomFields,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record SaveCustomerRequest(
    string Name,
    string? WhatsApp,
    string? SecondaryPhone,
    string? Instagram,
    string? Email,
    int? StageId,
    int? LeadSourceId,
    int? AssignedUserId,
    IReadOnlyList<int>? TreatmentIds,
    DateOnly? LastContactDate,
    DateOnly? NextFollowUpDate,
    string? Notes,
    /// <summary>Keyed by custom field key. Missing or null clears the value.</summary>
    Dictionary<string, JsonElement>? CustomFields);

public record ActivityDto(long Id, string Action, string? UserName, DateTime CreatedAt, JsonElement? Details);

/// <summary>Returned with a 409 when a WhatsApp number or Instagram name already belongs to a customer.</summary>
public record DuplicateCustomerDto(int ExistingCustomerId, string ExistingCustomerName, string MatchedOn);
