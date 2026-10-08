using System.Security.Claims;
using System.Text.Json;
using DoctorCrm.Api.Authentication;
using DoctorCrm.Api.Authorization;
using DoctorCrm.Api.Data;
using DoctorCrm.Api.DTOs;
using DoctorCrm.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace DoctorCrm.Api.Services;

/// <summary>
/// The dashboard's figures, all read live from the database (never invented, spec §10). Dates
/// are the clinic's local days. Each section is only filled for users allowed to see its data.
/// </summary>
public class DashboardService(AppDbContext db, ClinicClock clock)
{
    private const int FollowUpWindowDays = 7;
    private const int FollowUpListSize = 8;
    private const int ActivitySize = 10;

    /// <summary>Statuses that are real consultations; cancelled and rescheduled bookings are not.</summary>
    private static readonly BookingStatus[] Consultations = [BookingStatus.Booked, BookingStatus.Completed, BookingStatus.NoShow];

    public async Task<DashboardDto> GetAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var canBookings = user.HasClaim(CrmClaims.Permission, Permissions.BookingsView);
        var canCustomers = user.HasClaim(CrmClaims.Permission, Permissions.CustomersView);
        var today = await clock.TodayAsync(ct);

        return new DashboardDto(
            today,
            canBookings ? await BookingStatsAsync(today, ct) : null,
            canCustomers ? await CustomerStatsAsync(today, ct) : null,
            canBookings ? await TodaysAppointmentsAsync(today, ct) : null,
            canCustomers ? await StagesAsync(ct) : null,
            canCustomers ? await FollowUpsAsync(today, ct) : null,
            canCustomers ? await ActivityAsync(canBookings, ct) : null);
    }

    private async Task<DashboardBookingStatsDto> BookingStatsAsync(DateOnly today, CancellationToken ct)
    {
        var yesterday = today.AddDays(-1);
        var weekEnd = today.AddDays(FollowUpWindowDays);
        var counts = await db.Bookings.AsNoTracking()
            .Where(b => b.BookingDate >= yesterday && Consultations.Contains(b.Status))
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Today = g.Count(b => b.BookingDate == today),
                Yesterday = g.Count(b => b.BookingDate == yesterday),
                StillBooked = g.Count(b => b.BookingDate == today && b.Status == BookingStatus.Booked),
                Upcoming = g.Count(b => b.BookingDate > today && b.Status == BookingStatus.Booked),
                ThisWeek = g.Count(b => b.BookingDate > today && b.BookingDate <= weekEnd && b.Status == BookingStatus.Booked),
            })
            .SingleOrDefaultAsync(ct);

        return counts is null
            ? new DashboardBookingStatsDto(0, 0, 0, 0, 0)
            : new DashboardBookingStatsDto(counts.Today, counts.Yesterday, counts.StillBooked, counts.Upcoming, counts.ThisWeek);
    }

    private async Task<DashboardCustomerStatsDto> CustomerStatsAsync(DateOnly today, CancellationToken ct)
    {
        var startOfToday = await clock.StartOfDayUtcAsync(today, ct);
        var active = db.Customers.AsNoTracking().Where(c => c.IsActive);

        var due = await active.CountAsync(c => c.NextFollowUpDate <= today, ct);
        var overdue = await active.CountAsync(c => c.NextFollowUpDate < today, ct);
        var potential = await active.CountAsync(c => c.Stage.SystemKey == StageKeys.Interested || c.Stage.SystemKey == StageKeys.FollowUp, ct);
        var newToday = await active.CountAsync(c => c.CreatedAt >= startOfToday, ct);
        return new DashboardCustomerStatsDto(due, overdue, potential, newToday);
    }

    private async Task<IReadOnlyList<DashboardAppointmentDto>> TodaysAppointmentsAsync(DateOnly today, CancellationToken ct) =>
        await db.Bookings.AsNoTracking()
            .Where(b => b.BookingDate == today && Consultations.Contains(b.Status))
            .OrderBy(b => b.StartTime).ThenBy(b => b.Id)
            .Select(b => new DashboardAppointmentDto(
                b.Id,
                b.StartTime,
                b.EndTime,
                b.Status.ToString(),
                new NamedRef(b.Customer.Id, b.Customer.Name),
                b.Treatments.OrderBy(t => t.Treatment.DisplayOrder).Select(t => t.Treatment.Name).ToList(),
                b.Doctor != null ? b.Doctor.FullName : null))
            .AsSplitQuery()
            .ToListAsync(ct);

    private async Task<IReadOnlyList<DashboardStageDto>> StagesAsync(CancellationToken ct) =>
        await db.Stages.AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.DisplayOrder).ThenBy(s => s.Name)
            .Select(s => new DashboardStageDto(s.Id, s.Name, s.Color, s.SystemKey,
                db.Customers.Count(c => c.StageId == s.Id && c.IsActive)))
            .ToListAsync(ct);

    /// <summary>Overdue first, then due today and the coming week.</summary>
    private async Task<IReadOnlyList<DashboardFollowUpDto>> FollowUpsAsync(DateOnly today, CancellationToken ct)
    {
        var until = today.AddDays(FollowUpWindowDays - 1);
        return await db.Customers.AsNoTracking()
            .Where(c => c.IsActive && c.NextFollowUpDate != null && c.NextFollowUpDate <= until)
            .OrderBy(c => c.NextFollowUpDate).ThenBy(c => c.Name)
            .Take(FollowUpListSize)
            .Select(c => new DashboardFollowUpDto(
                c.Id,
                c.Name,
                c.WhatsAppNumber,
                c.NextFollowUpDate!.Value,
                new StageRef(c.Stage.Id, c.Stage.Name, c.Stage.Color, c.Stage.SystemKey),
                c.Treatments.OrderBy(t => t.Treatment.DisplayOrder).Select(t => t.Treatment.Name).ToList()))
            .AsSplitQuery()
            .ToListAsync(ct);
    }

    /// <summary>
    /// Recent customer and booking events, without the echoes: a payment taken while completing a
    /// consultation is part of the completion entry, and an automatic stage move is implied by the
    /// booking or completion that caused it. The customer's own timeline still shows both.
    /// </summary>
    private async Task<IReadOnlyList<DashboardActivityDto>> ActivityAsync(bool includeBookings, CancellationToken ct)
    {
        var rows = await db.AuditLogs.AsNoTracking()
            .Where(a => (a.EntityType == nameof(Customer)
                         && !(a.Action == "Stage Changed" && a.Metadata != null && EF.Functions.JsonExists(a.Metadata, "automatic")))
                        || (includeBookings && a.EntityType == nameof(Booking)
                            && (a.Action != "Payment Recorded" || (a.Metadata != null && (EF.Functions.JsonExists(a.Metadata, "later") || EF.Functions.JsonExists(a.Metadata, "settledPending"))))))
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Take(ActivitySize)
            .Select(a => new { a.Id, a.Action, a.EntityType, a.EntityId, UserName = a.User != null ? a.User.FullName : null, a.CreatedAt, a.Metadata })
            .ToListAsync(ct);

        // Resolve each entry to its customer: directly, or through the booking.
        var bookingIds = rows.Where(r => r.EntityType == nameof(Booking)).Select(r => ParseId(r.EntityId)).OfType<int>().Distinct().ToList();
        var bookingCustomer = await db.Bookings.AsNoTracking()
            .Where(b => bookingIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => b.CustomerId, ct);

        var customerIds = rows.Where(r => r.EntityType == nameof(Customer)).Select(r => ParseId(r.EntityId)).OfType<int>()
            .Concat(bookingCustomer.Values).Distinct().ToList();
        var names = await db.Customers.AsNoTracking()
            .Where(c => customerIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        return rows.Select(r =>
        {
            var entityId = ParseId(r.EntityId);
            int? bookingId = r.EntityType == nameof(Booking) ? entityId : null;
            int? customerId = bookingId is { } bid ? bookingCustomer.GetValueOrDefault(bid) : entityId;
            var customer = customerId is { } cid && names.TryGetValue(cid, out var name) ? new NamedRef(cid, name) : null;
            return new DashboardActivityDto(r.Id, r.Action, r.UserName, r.CreatedAt,
                r.Metadata is null ? null : JsonDocument.Parse(r.Metadata).RootElement.Clone(), customer, bookingId);
        }).ToList();
    }

    private static int? ParseId(string? id) => int.TryParse(id, out var v) ? v : null;
}
