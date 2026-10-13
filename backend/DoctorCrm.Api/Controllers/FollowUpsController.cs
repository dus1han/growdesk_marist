using DoctorCrm.Api.Authorization;
using DoctorCrm.Api.DTOs;
using DoctorCrm.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace DoctorCrm.Api.Controllers;

/// <summary>Follow-ups: the daily queue, reminders, logging a follow-up, and the settings.</summary>
[ApiController]
[Route("api/follow-ups")]
[HasPermission(Permissions.CustomersView)]
public class FollowUpsController(FollowUpService followUps) : ControllerBase
{
    /// <summary>Due today and overdue. scope=mine (default: own and unassigned customers) or all.</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<FollowUpQueueDto>>> Queue([FromQuery] string? scope, CancellationToken ct) =>
        Ok(ApiResponse<FollowUpQueueDto>.Ok(await followUps.QueueAsync(scope, User.GetUserId()!.Value, ct)));

    /// <summary>Counts and new enquiries for the sidebar badge and desktop reminders.</summary>
    [HttpGet("summary")]
    public async Task<ActionResult<ApiResponse<FollowUpSummaryDto>>> Summary(CancellationToken ct) =>
        Ok(ApiResponse<FollowUpSummaryDto>.Ok(await followUps.SummaryAsync(User.GetUserId()!.Value, ct)));

    [HttpGet("settings")]
    public async Task<ActionResult<ApiResponse<FollowUpSettingsDto>>> Settings(CancellationToken ct) =>
        Ok(ApiResponse<FollowUpSettingsDto>.Ok(await followUps.GetSettingsAsync(ct)));

    [HttpPut("settings")]
    [HasPermission(Permissions.SettingsManage)]
    public async Task<ActionResult<ApiResponse<FollowUpSettingsDto>>> SaveSettings(FollowUpSettingsDto request, CancellationToken ct) =>
        Ok(ApiResponse<FollowUpSettingsDto>.Ok(await followUps.SaveSettingsAsync(request, User.GetUserId()!.Value, ct), "Follow-up settings saved."));

    /// <summary>Records how a follow-up went and schedules the next one.</summary>
    [HttpPost("/api/customers/{customerId:int}/follow-ups")]
    [HasPermission(Permissions.CustomersManage)]
    public async Task<ActionResult<ApiResponse<FollowUpLoggedDto>>> Log(int customerId, LogFollowUpRequest request, CancellationToken ct) =>
        Ok(ApiResponse<FollowUpLoggedDto>.Ok(await followUps.LogAsync(customerId, request, User.GetUserId()!.Value, ct), "Follow-up logged."));
}
