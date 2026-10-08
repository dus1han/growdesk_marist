namespace DoctorCrm.Api.DTOs;

/// <summary>Query string for GET /api/recycle-bin.</summary>
public record RecycleBinQuery
{
    /// <summary>customer, booking, payment, treatment, stage, lead-source, payment-method or cancellation-reason.</summary>
    public string? Type { get; set; }
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

/// <summary>A deleted record, newest first. Type is the URL name, TypeLabel what to show.</summary>
public record RecycleBinItemDto(
    string Type,
    string TypeLabel,
    int Id,
    string Title,
    string? Detail,
    DateTime DeletedAt,
    string? DeletedBy);
