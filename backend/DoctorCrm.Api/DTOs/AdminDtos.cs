namespace DoctorCrm.Api.DTOs;

// ---- Lookup lists (treatments, stages, lead sources, cancellation reasons, payment methods) ----

/// <summary>
/// One shape for every admin list. <see cref="Description"/> is only used by treatments,
/// <see cref="Color"/> and <see cref="SystemKey"/> only by stages; other lists return null.
/// </summary>
public record LookupItemDto(
    int Id,
    string Name,
    string? Description,
    string? Color,
    string? SystemKey,
    bool IsActive,
    int DisplayOrder);

public record SaveLookupItemRequest(string Name, string? Description, string? Color);

public record SetActiveRequest(bool IsActive);

public record ReorderRequest(IReadOnlyList<int> Ids);

// ---- Users ----------------------------------------------------------------------------------

public record RoleDto(int Id, string Name, string? Description);

public record UserDto(
    int Id,
    string FullName,
    string Username,
    string? Email,
    int? RoleId,
    string? RoleName,
    bool IsActive,
    bool MustChangePassword,
    DateTime? LastLoginAt,
    DateTime CreatedAt,
    bool IsPlatformOwner = false);

public record CreateUserRequest(string FullName, string Username, string? Email, int RoleId, string Password);

public record UpdateUserRequest(string FullName, string Username, string? Email, int RoleId);

public record ResetPasswordRequest(string NewPassword);

// ---- Custom fields ----------------------------------------------------------------------------

public record CustomFieldOptionDto(int Id, string Label, int DisplayOrder, bool IsActive);

public record CustomFieldDto(
    int Id,
    string Key,
    string Label,
    string FieldType,
    bool IsRequired,
    bool IsActive,
    int DisplayOrder,
    IReadOnlyList<CustomFieldOptionDto> Options);

/// <summary>Options are the complete active list: omitted existing options are deactivated.</summary>
public record SaveCustomFieldOption(int? Id, string Label);

public record SaveCustomFieldRequest(
    string Label,
    string FieldType,
    bool IsRequired,
    IReadOnlyList<SaveCustomFieldOption>? Options);

// ---- Capture tool configuration -----------------------------------------------------------------

public record CaptureFieldDto(
    string Key,
    string Label,
    string Type,
    bool IsCustom,
    bool IsEnabled,
    bool IsRequired,
    int DisplayOrder);

public record SaveCaptureField(string Key, bool IsEnabled, bool IsRequired);

/// <summary>Every field, in the desired order.</summary>
public record SaveCaptureFieldsRequest(IReadOnlyList<SaveCaptureField> Fields);

// ---- System settings -------------------------------------------------------------------------

public record SystemSettingsDto(string CrmName, string Tagline, string? LogoUrl, string Currency, string TimeZone);
