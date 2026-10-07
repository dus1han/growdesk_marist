using DoctorCrm.Api.Authorization;
using DoctorCrm.Api.DTOs;
using DoctorCrm.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace DoctorCrm.Api.Controllers;

[ApiController]
[Route("api/audit-logs")]
[HasPermission(Permissions.AuditView)]
public class AuditLogsController(AuditLogService auditLogs) : ControllerBase
{
    /// <summary>Audit entries, newest first.</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<AuditLogDto>>>> List([FromQuery] AuditQuery query, CancellationToken ct) =>
        Ok(ApiResponse<PagedResult<AuditLogDto>>.Ok(await auditLogs.ListAsync(query, ct)));

    /// <summary>Choices for the action and user filters.</summary>
    [HttpGet("filters")]
    public async Task<ActionResult<ApiResponse<AuditFiltersDto>>> Filters(CancellationToken ct) =>
        Ok(ApiResponse<AuditFiltersDto>.Ok(await auditLogs.FiltersAsync(ct)));
}
