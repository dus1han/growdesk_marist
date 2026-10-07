using System.Text.Json;

namespace DoctorCrm.Api.DTOs;

/// <summary>Query string for GET /api/audit-logs. Every filter is optional and they combine (AND).</summary>
public class AuditQuery
{
    /// <summary>Matches the user's name, the action, or the customer the entry is about.</summary>
    public string? Search { get; set; }
    public int? UserId { get; set; }
    public string? Action { get; set; }
    /// <summary>Clinic-local dates, inclusive.</summary>
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

/// <summary>
/// One audit entry. <c>Subject</c> names the record it is about where that can be looked up (a
/// customer's name, a user's name); <c>CustomerId</c> links customer and booking entries to the profile.
/// </summary>
public record AuditLogDto(
    long Id,
    DateTime CreatedAt,
    int? UserId,
    string? UserName,
    string Action,
    string EntityType,
    string? EntityId,
    string? Subject,
    int? CustomerId,
    JsonElement? Details);

/// <summary>The choices for the audit log filters: every action recorded so far, and every user.</summary>
public record AuditFiltersDto(IReadOnlyList<string> Actions, IReadOnlyList<NamedRef> Users);
