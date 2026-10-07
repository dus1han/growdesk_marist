using DoctorCrm.Api.Authentication;
using DoctorCrm.Api.Authorization;
using DoctorCrm.Api.DTOs;
using DoctorCrm.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoctorCrm.Api.Controllers;

/// <summary>The clinic's GrowDesk subscription, paid through Stripe.</summary>
[ApiController]
[Route("api/billing")]
[AllowWhileSubscriptionBlocked]
public class BillingController(BillingService billing, BillingSettingsService settings) : ControllerBase
{
    /// <summary>The app's address as the browser sees it, for Stripe's return links.</summary>
    private string? BrowserOrigin => Request.Headers.Origin.FirstOrDefault() ?? Request.Headers.Referer.FirstOrDefault();

    /// <summary>Whether the system is blocked and the days left to pay. Any signed-in user.</summary>
    [HttpGet("notice")]
    [Authorize]
    [AllowWhilePasswordChangeRequired]
    public async Task<ActionResult<ApiResponse<BillingNoticeDto>>> Notice(CancellationToken ct) =>
        Ok(ApiResponse<BillingNoticeDto>.Ok(await billing.GetNoticeAsync(User.HasClaim(CrmClaims.Permission, Permissions.BillingManage), ct)));

    /// <summary>Plan, status, renewal date and card on file.</summary>
    [HttpGet]
    [HasPermission(Permissions.BillingManage)]
    public async Task<ActionResult<ApiResponse<BillingOverviewDto>>> Overview(CancellationToken ct) =>
        Ok(ApiResponse<BillingOverviewDto>.Ok(await billing.GetOverviewAsync(ct)));

    /// <summary>Subscription payment history (Stripe invoices), newest first.</summary>
    [HttpGet("invoices")]
    [HasPermission(Permissions.BillingManage)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<BillingInvoice>>>> Invoices(CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<BillingInvoice>>.Ok(await billing.ListInvoicesAsync(ct)));

    /// <summary>Refreshes the subscription from Stripe (e.g. after returning from a Stripe page).</summary>
    [HttpPost("sync")]
    [HasPermission(Permissions.BillingManage)]
    public async Task<ActionResult<ApiResponse<BillingOverviewDto>>> Sync(CancellationToken ct)
    {
        await billing.SyncAsync(ct);
        return await Overview(ct);
    }

    /// <summary>Starts the subscription: returns the Stripe Checkout page to enter the card on.</summary>
    [HttpPost("subscribe")]
    [HasPermission(Permissions.BillingManage)]
    public async Task<ActionResult<ApiResponse<BillingRedirectDto>>> Subscribe(CancellationToken ct) =>
        Ok(ApiResponse<BillingRedirectDto>.Ok(await billing.StartSubscriptionAsync(User.GetUserId()!.Value, BrowserOrigin, ct)));

    /// <summary>Pays the overdue amount now with the card on file (or on Stripe's page if it is declined).</summary>
    [HttpPost("pay-now")]
    [HasPermission(Permissions.BillingManage)]
    public async Task<ActionResult<ApiResponse<PayNowResultDto>>> PayNow(CancellationToken ct) =>
        Ok(ApiResponse<PayNowResultDto>.Ok(await billing.PayNowAsync(User.GetUserId()!.Value, BrowserOrigin, ct)));

    /// <summary>Stripe's billing portal, to change the card on file.</summary>
    [HttpPost("portal")]
    [HasPermission(Permissions.BillingManage)]
    public async Task<ActionResult<ApiResponse<BillingRedirectDto>>> Portal(CancellationToken ct) =>
        Ok(ApiResponse<BillingRedirectDto>.Ok(await billing.PortalAsync(BrowserOrigin, ct)));

    /// <summary>Stripe Settings: keys (never returned), price and grace period. Platform owners only.</summary>
    [HttpGet("settings")]
    [HasPermission(Permissions.PlatformBilling)]
    public async Task<ActionResult<ApiResponse<BillingSettingsDto>>> GetSettings(CancellationToken ct) =>
        Ok(ApiResponse<BillingSettingsDto>.Ok(await settings.GetAsync(BrowserOrigin, ct)));

    /// <summary>Saves Stripe Settings after checking the key and price with Stripe.</summary>
    [HttpPut("settings")]
    [HasPermission(Permissions.PlatformBilling)]
    public async Task<ActionResult<ApiResponse<BillingSettingsDto>>> SaveSettings(SaveBillingSettingsRequest request, CancellationToken ct) =>
        Ok(ApiResponse<BillingSettingsDto>.Ok(await settings.SaveAsync(request, User.GetUserId()!.Value, BrowserOrigin, ct), "Stripe settings saved."));

    /// <summary>Stripe webhook endpoint (signature-verified). Configure it in the Stripe dashboard.</summary>
    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<object>>> Webhook(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        var json = await reader.ReadToEndAsync(ct);
        await billing.HandleWebhookAsync(json, Request.Headers["Stripe-Signature"].ToString(), ct);
        return Ok(ApiResponse.Ok());
    }
}
