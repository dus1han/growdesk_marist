using DoctorCrm.Api.Authorization;
using DoctorCrm.Api.DTOs;
using DoctorCrm.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace DoctorCrm.Api.Controllers;

/// <summary>Administration → Recycle Bin: deleted records, and restoring them.</summary>
[ApiController]
[Route("api/recycle-bin")]
[HasPermission(Permissions.RecordsDelete)]
public class RecycleBinController(RecycleBinService bin) : ControllerBase
{
    /// <summary>Deleted records, most recently deleted first.</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<RecycleBinItemDto>>>> List([FromQuery] RecycleBinQuery query, CancellationToken ct) =>
        Ok(ApiResponse<PagedResult<RecycleBinItemDto>>.Ok(await bin.ListAsync(query, ct)));

    /// <summary>Brings a record back, once what it belongs to is back.</summary>
    [HttpPost("{type}/{id:int}/restore")]
    public async Task<ActionResult<ApiResponse<object>>> Restore(string type, int id, CancellationToken ct)
    {
        await bin.RestoreAsync(type, id, User.GetUserId()!.Value, ct);
        return Ok(ApiResponse.Ok("Restored."));
    }
}
