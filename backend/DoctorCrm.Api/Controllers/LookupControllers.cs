using DoctorCrm.Api.Authorization;
using DoctorCrm.Api.DTOs;
using DoctorCrm.Api.Entities;
using DoctorCrm.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace DoctorCrm.Api.Controllers;

/// <summary>
/// Shared endpoints for the admin-managed lists. Any signed-in user can read them (forms need
/// them); changing them needs the settings permission.
/// </summary>
[ApiController]
public abstract class LookupControllerBase<T>(LookupService<T> service) : ControllerBase
    where T : class, ILookupEntity, ISoftDeletable, new()
{
    /// <summary>Active items in display order; <c>includeInactive=true</c> for the admin screens.</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<LookupItemDto>>>> List(
        [FromQuery] bool includeInactive, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<LookupItemDto>>.Ok(await service.ListAsync(includeInactive, ct)));

    [HttpPost]
    [HasPermission(Permissions.SettingsManage)]
    public async Task<ActionResult<ApiResponse<LookupItemDto>>> Create(SaveLookupItemRequest request, CancellationToken ct) =>
        Ok(ApiResponse<LookupItemDto>.Ok(await service.CreateAsync(request, User.GetUserId(), ct),
            $"{LookupService<T>.DisplayName} added."));

    [HttpPut("{id:int}")]
    [HasPermission(Permissions.SettingsManage)]
    public async Task<ActionResult<ApiResponse<LookupItemDto>>> Update(int id, SaveLookupItemRequest request, CancellationToken ct) =>
        Ok(ApiResponse<LookupItemDto>.Ok(await service.UpdateAsync(id, request, User.GetUserId(), ct),
            $"{LookupService<T>.DisplayName} saved."));

    [HttpPatch("{id:int}/active")]
    [HasPermission(Permissions.SettingsManage)]
    public async Task<ActionResult<ApiResponse<LookupItemDto>>> SetActive(int id, SetActiveRequest request, CancellationToken ct) =>
        Ok(ApiResponse<LookupItemDto>.Ok(await service.SetActiveAsync(id, request.IsActive, User.GetUserId(), ct)));

    /// <summary>To the recycle bin, once nothing uses it (otherwise deactivate it).</summary>
    [HttpDelete("{id:int}")]
    [HasPermission(Permissions.RecordsDelete)]
    public async Task<ActionResult<ApiResponse<object>>> Delete(int id, [FromServices] RecycleBinService bin, CancellationToken ct)
    {
        await bin.DeleteLookupAsync<T>(id, User.GetUserId()!.Value, ct);
        return Ok(ApiResponse.Ok($"{LookupService<T>.DisplayName} deleted."));
    }

    /// <summary>Sets the display order. <c>ids</c> must list every item, active or not.</summary>
    [HttpPut("reorder")]
    [HasPermission(Permissions.SettingsManage)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<LookupItemDto>>>> Reorder(ReorderRequest request, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<LookupItemDto>>.Ok(await service.ReorderAsync(request.Ids, User.GetUserId(), ct)));
}

[Route("api/treatments")]
public class TreatmentsController(LookupService<Treatment> s) : LookupControllerBase<Treatment>(s);

[Route("api/stages")]
public class StagesController(LookupService<Stage> s) : LookupControllerBase<Stage>(s);

[Route("api/lead-sources")]
public class LeadSourcesController(LookupService<LeadSource> s) : LookupControllerBase<LeadSource>(s);

[Route("api/cancellation-reasons")]
public class CancellationReasonsController(LookupService<CancellationReason> s) : LookupControllerBase<CancellationReason>(s);

[Route("api/payment-methods")]
public class PaymentMethodsController(LookupService<PaymentMethod> s) : LookupControllerBase<PaymentMethod>(s);
