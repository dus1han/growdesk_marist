using System.Text.Json;
using DoctorCrm.Api.Data;
using DoctorCrm.Api.DTOs;
using DoctorCrm.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace DoctorCrm.Api.Services;

/// <summary>Reads the audit log for Administration → Audit Log. Writing stays in <see cref="AuditService"/>.</summary>
public class AuditLogService(AppDbContext db, ClinicClock clock)
{
    public async Task<PagedResult<AuditLogDto>> ListAsync(AuditQuery q, CancellationToken ct)
    {
        var page = Math.Max(1, q.Page);
        var size = Math.Clamp(q.PageSize, 1, 200);
        var query = db.AuditLogs.AsNoTracking();

        if (q.UserId is { } userId) query = query.Where(a => a.UserId == userId);
        if (!string.IsNullOrWhiteSpace(q.Action)) query = query.Where(a => a.Action == q.Action);
        if (q.From is { } from)
        {
            var start = await clock.StartOfDayUtcAsync(from, ct);
            query = query.Where(a => a.CreatedAt >= start);
        }
        if (q.To is { } to)
        {
            var end = await clock.StartOfDayUtcAsync(to.AddDays(1), ct);
            query = query.Where(a => a.CreatedAt < end);
        }
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var like = $"%{q.Search.Trim()}%";
            query = query.Where(a =>
                (a.User != null && (EF.Functions.ILike(a.User.FullName, like) || EF.Functions.ILike(a.User.Username, like)))
                || EF.Functions.ILike(a.Action, like)
                || (a.EntityType == nameof(Customer) && db.Customers.Any(c => c.Id.ToString() == a.EntityId && EF.Functions.ILike(c.Name, like)))
                || (a.EntityType == nameof(Booking) && db.Bookings.Any(b => b.Id.ToString() == a.EntityId && EF.Functions.ILike(b.Customer.Name, like))));
        }

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Skip((page - 1) * size).Take(size)
            .Select(a => new { a.Id, a.CreatedAt, a.UserId, UserName = a.User != null ? a.User.FullName : null, a.Action, a.EntityType, a.EntityId, a.Metadata })
            .ToListAsync(ct);

        // Name the record each entry is about, in one lookup per kind.
        int[] Ids(string type) => rows.Where(r => r.EntityType == type && int.TryParse(r.EntityId, out _))
            .Select(r => int.Parse(r.EntityId!)).Distinct().ToArray();
        var customerIds = Ids(nameof(Customer));
        var bookingIds = Ids(nameof(Booking));
        var userIds = Ids(nameof(User));
        var customers = await db.Customers.AsNoTracking().Where(c => customerIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var bookings = await db.Bookings.AsNoTracking().Where(b => bookingIds.Contains(b.Id))
            .Select(b => new { b.Id, b.CustomerId, b.Customer.Name })
            .ToDictionaryAsync(b => b.Id, ct);
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        var items = rows.Select(r =>
        {
            string? subject = null;
            int? customerId = null;
            if (int.TryParse(r.EntityId, out var id))
            {
                switch (r.EntityType)
                {
                    case nameof(Customer) when customers.TryGetValue(id, out var name):
                        subject = name;
                        customerId = id;
                        break;
                    case nameof(Booking) when bookings.TryGetValue(id, out var booking):
                        subject = booking.Name;
                        customerId = booking.CustomerId;
                        break;
                    case nameof(User) when users.TryGetValue(id, out var userName):
                        subject = userName;
                        break;
                }
            }
            return new AuditLogDto(r.Id, r.CreatedAt, r.UserId, r.UserName, r.Action, r.EntityType, r.EntityId, subject, customerId,
                r.Metadata is null ? null : JsonDocument.Parse(r.Metadata).RootElement.Clone());
        }).ToList();

        return new PagedResult<AuditLogDto>(items, page, size, total);
    }

    public async Task<AuditFiltersDto> FiltersAsync(CancellationToken ct)
    {
        var actions = await db.AuditLogs.AsNoTracking().Select(a => a.Action).Distinct().OrderBy(a => a).ToListAsync(ct);
        var users = await db.Users.AsNoTracking().OrderBy(u => u.FullName)
            .Select(u => new NamedRef(u.Id, u.FullName)).ToListAsync(ct);
        return new AuditFiltersDto(actions, users);
    }
}
