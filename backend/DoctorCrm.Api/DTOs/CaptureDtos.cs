using System.Text.Json;

namespace DoctorCrm.Api.DTOs;

// ---- Capture tool connections (admin) --------------------------------------------------------

public record CaptureClientDto(
    int Id,
    string Name,
    string ClientId,
    bool IsActive,
    DateTime CreatedAt,
    string? CreatedBy,
    DateTime? LastUsedAt,
    DateTime? RevokedAt,
    /// <summary>The toolbar version this PC last connected with.</summary>
    string? ExtensionVersion,
    /// <summary>"Toolbar" or "Bot".</summary>
    string Kind);

/// <summary><c>Kind</c> is "Toolbar" (default) or "Bot".</summary>
public record CreateCaptureClientRequest(string Name, string? Kind = null);

/// <summary>Returned once, when a connection is created. The secret cannot be read again.</summary>
public record CaptureClientCreatedDto(CaptureClientDto Client, string ClientSecret);

// ---- Capture API (the tool) ------------------------------------------------------------------

/// <summary>Client credentials exchanged for a short-lived access token.</summary>
public record CaptureTokenRequest(string ClientId, string ClientSecret);

public record CaptureTokenDto(string AccessToken, string TokenType, int ExpiresIn);

/// <summary>
/// One field the capture tool shows (spec §33). <c>Options</c> is set for dropdown and
/// multi-select fields; built-in lists (treatments, stages, sources) have their own endpoints.
/// </summary>
public record CaptureConfigFieldDto(
    string Key,
    string Label,
    string Type,
    bool Enabled,
    bool Required,
    int Order,
    bool IsCustom,
    IReadOnlyList<CaptureOptionDto>? Options);

public record CaptureConfigDto(IReadOnlyList<CaptureConfigFieldDto> Fields);

public record CaptureOptionDto(int Id, string Label);

public record CaptureLookupDto(int Id, string Name, string? Color);

public record CaptureCustomFieldDto(string Key, string Label, string Type, IReadOnlyList<CaptureOptionDto> Options);

/// <summary>
/// A captured lead (spec §34). Only fields the admin enabled are used; the rest are ignored.
/// Custom field values are keyed by the field's key.
/// </summary>
public record CaptureCustomerRequest(
    string? Name,
    string? WhatsApp,
    string? SecondaryPhone,
    string? Instagram,
    string? Email,
    int? StageId,
    int? LeadSourceId,
    IReadOnlyList<int>? TreatmentIds,
    Dictionary<string, JsonElement>? CustomFields,
    string? Notes,
    /// <summary>
    /// The site the toolbar captured on: "whatsapp" or "instagram" (toolbar 1.0.13+). Sets the lead
    /// source; an older toolbar leaves it out and may send LeadSourceId instead.
    /// </summary>
    string? Source = null);

/// <summary>Tells the tool whether the lead was new (spec §35), plus anything it should show the user.</summary>
public record CaptureCustomerResultDto(int CustomerId, string Action, string CustomerName, IReadOnlyList<string> Warnings);
