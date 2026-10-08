using DoctorCrm.Api.Authorization;
using DoctorCrm.Api.DTOs;
using DoctorCrm.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace DoctorCrm.Api.Controllers;

[ApiController]
[Route("api/payments")]
[HasPermission(Permissions.PaymentsView)]
public class PaymentsController(PaymentService payments) : ControllerBase
{
    /// <summary>Payment entries (money received, balances waived), newest first.</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<PaymentListItemDto>>>> List([FromQuery] PaymentQuery query, CancellationToken ct) =>
        Ok(ApiResponse<PagedResult<PaymentListItemDto>>.Ok(await payments.ListAsync(query, ct)));

    /// <summary>Collected and waived in the range; outstanding as of now.</summary>
    [HttpGet("summary")]
    public async Task<ActionResult<ApiResponse<PaymentSummaryDto>>> Summary([FromQuery] PaymentQuery query, CancellationToken ct) =>
        Ok(ApiResponse<PaymentSummaryDto>.Ok(await payments.SummaryAsync(query, ct)));

    /// <summary>Completed consultations that still have a balance, oldest first.</summary>
    [HttpGet("outstanding")]
    public async Task<ActionResult<ApiResponse<PagedResult<OutstandingItemDto>>>> Outstanding([FromQuery] OutstandingQuery query, CancellationToken ct) =>
        Ok(ApiResponse<PagedResult<OutstandingItemDto>>.Ok(await payments.OutstandingAsync(query, ct)));

    /// <summary>To the recycle bin. The consultation's balance goes back up by the amount.</summary>
    [HttpDelete("{id:int}")]
    [HasPermission(Permissions.RecordsDelete)]
    public async Task<ActionResult<ApiResponse<object>>> Delete(int id, [FromServices] RecycleBinService bin, CancellationToken ct)
    {
        await bin.DeletePaymentAsync(id, User.GetUserId()!.Value, ct);
        return Ok(ApiResponse.Ok("Payment deleted."));
    }

    /// <summary>The filtered list as an Excel workbook, with a total row.</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] PaymentQuery query, CancellationToken ct)
    {
        var (bytes, fileName) = await payments.ExportAsync(query, User.GetUserId(), ct);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }
}

[ApiController]
[Route("api/bookings/{bookingId:int}/payments")]
[HasPermission(Permissions.PaymentsManage)]
public class BookingPaymentsController(PaymentService payments) : ControllerBase
{
    /// <summary>Money received: any amount up to the consultation's balance.</summary>
    [HttpPost]
    public async Task<ActionResult<ApiResponse<PaymentListItemDto>>> Record(int bookingId, RecordPaymentRequest request, CancellationToken ct) =>
        Ok(ApiResponse<PaymentListItemDto>.Ok(await payments.RecordAsync(bookingId, request, User.GetUserId(), ct), "Payment recorded."));

    /// <summary>Writes off the consultation's remaining balance.</summary>
    [HttpPost("waive")]
    public async Task<ActionResult<ApiResponse<PaymentListItemDto>>> Waive(int bookingId, CancellationToken ct) =>
        Ok(ApiResponse<PaymentListItemDto>.Ok(await payments.WaiveAsync(bookingId, User.GetUserId(), ct), "Balance waived."));
}
