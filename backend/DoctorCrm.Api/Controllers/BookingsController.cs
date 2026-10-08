using DoctorCrm.Api.Authorization;
using DoctorCrm.Api.Data;
using DoctorCrm.Api.DTOs;
using DoctorCrm.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoctorCrm.Api.Controllers;

[ApiController]
[Route("api/bookings")]
[HasPermission(Permissions.BookingsView)]
public class BookingsController(BookingService bookings, BookingExportService export) : ControllerBase
{
    /// <summary>
    /// The filtered list as an Excel workbook (same filters as GET /api/bookings). Charge and
    /// payment columns are included only for users who may see payments.
    /// </summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] BookingQuery query, CancellationToken ct)
    {
        var includePayments = User.HasClaim(Authentication.CrmClaims.Permission, Permissions.PaymentsView);
        var (bytes, fileName) = await export.CreateAsync(query, includePayments, User.GetUserId(), ct);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    /// <summary>Bookings by date range, customer, doctor and status. Used by the calendar and lists.</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<BookingListItemDto>>>> List([FromQuery] BookingQuery query, CancellationToken ct) =>
        Ok(ApiResponse<PagedResult<BookingListItemDto>>.Ok(await bookings.ListAsync(query, ct)));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ApiResponse<BookingDetailDto>>> Get(int id, CancellationToken ct) =>
        Ok(ApiResponse<BookingDetailDto>.Ok(await bookings.GetAsync(id, ct)));

    /// <summary>Books a consultation. 409 with the conflicting booking when the time overlaps.</summary>
    [HttpPost]
    [HasPermission(Permissions.BookingsManage)]
    public async Task<ActionResult<ApiResponse<BookingDetailDto>>> Create(CreateBookingRequest request, CancellationToken ct) =>
        Ok(ApiResponse<BookingDetailDto>.Ok(await bookings.CreateAsync(request, User.GetUserId(), ct), "Consultation booked."));

    [HttpPut("{id:int}")]
    [HasPermission(Permissions.BookingsManage)]
    public async Task<ActionResult<ApiResponse<BookingDetailDto>>> Update(int id, UpdateBookingRequest request, CancellationToken ct) =>
        Ok(ApiResponse<BookingDetailDto>.Ok(await bookings.UpdateAsync(id, request, User.GetUserId(), ct), "Booking saved."));

    [HttpPost("{id:int}/complete")]
    [HasPermission(Permissions.BookingsComplete)]
    public async Task<ActionResult<ApiResponse<BookingDetailDto>>> Complete(int id, CompleteBookingRequest request, CancellationToken ct) =>
        Ok(ApiResponse<BookingDetailDto>.Ok(await bookings.CompleteAsync(id, request, User.GetUserId(), ct), "Consultation completed."));

    /// <summary>Returns the NEW booking. The original is kept with status Rescheduled and no charge.</summary>
    [HttpPost("{id:int}/reschedule")]
    [HasPermission(Permissions.BookingsManage)]
    public async Task<ActionResult<ApiResponse<BookingDetailDto>>> Reschedule(int id, RescheduleBookingRequest request, CancellationToken ct) =>
        Ok(ApiResponse<BookingDetailDto>.Ok(await bookings.RescheduleAsync(id, request, User.GetUserId(), ct), "Appointment rescheduled."));

    [HttpPost("{id:int}/cancel")]
    [HasPermission(Permissions.BookingsManage)]
    public async Task<ActionResult<ApiResponse<BookingDetailDto>>> Cancel(int id, CancelBookingRequest request, CancellationToken ct) =>
        Ok(ApiResponse<BookingDetailDto>.Ok(await bookings.CancelAsync(id, request, User.GetUserId(), ct), "Booking cancelled."));

    [HttpPost("{id:int}/no-show")]
    [HasPermission(Permissions.BookingsManage)]
    public async Task<ActionResult<ApiResponse<BookingDetailDto>>> NoShow(int id, CancellationToken ct) =>
        Ok(ApiResponse<BookingDetailDto>.Ok(await bookings.MarkNoShowAsync(id, User.GetUserId(), ct), "Marked as no-show."));

    /// <summary>To the recycle bin. Only once the booking has no payments left.</summary>
    [HttpDelete("{id:int}")]
    [HasPermission(Permissions.RecordsDelete)]
    public async Task<ActionResult<ApiResponse<object>>> Delete(int id, [FromServices] RecycleBinService bin, CancellationToken ct)
    {
        await bin.DeleteBookingAsync(id, User.GetUserId()!.Value, ct);
        return Ok(ApiResponse.Ok("Booking deleted."));
    }
}

/// <summary>Active users in the Doctor role, for the booking form and calendar filter.</summary>
[ApiController]
[Route("api/doctor-options")]
[HasPermission(Permissions.BookingsView)]
public class DoctorOptionsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<NamedRef>>>> List(CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<NamedRef>>.Ok(await db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.UserRoles.Any(ur => ur.Role.Name == Roles.Doctor))
            .OrderBy(u => u.FullName)
            .Select(u => new NamedRef(u.Id, u.FullName)).ToListAsync(ct)));
}

/// <summary>Currency, time zone and the clinic's "today", for any signed-in user.</summary>
[ApiController]
[Route("api/settings/locale")]
public class LocaleController(SettingsService settings, ClinicClock clock) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<LocaleDto>>> Get(CancellationToken ct)
    {
        var s = await settings.GetAsync(ct);
        return Ok(ApiResponse<LocaleDto>.Ok(new LocaleDto(s.Currency, s.TimeZone, await clock.TodayAsync(ct))));
    }
}
