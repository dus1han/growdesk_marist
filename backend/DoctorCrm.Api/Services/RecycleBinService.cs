using DoctorCrm.Api.Data;
using DoctorCrm.Api.DTOs;
using DoctorCrm.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace DoctorCrm.Api.Services;

/// <summary>
/// Deleting records to the recycle bin, and restoring them. Deleting goes lowest level first: a
/// payment any time; a booking once it has no payments; a customer once they have no bookings;
/// an admin list item once nothing uses it. Restoring goes the other way: a record comes back
/// only once what it belongs to is back. Nothing is erased: deleted records stay in the database,
/// hidden by a global filter, and every delete and restore is in the audit log.
/// </summary>
public class RecycleBinService(AppDbContext db, AuditService audit)
{
    /// <summary>Record types, as used in URLs (/api/recycle-bin/{type}/{id}/restore).</summary>
    public static readonly IReadOnlyDictionary<string, string> Types = new Dictionary<string, string>
    {
        ["customer"] = "Customer",
        ["booking"] = "Booking",
        ["payment"] = "Payment",
        ["treatment"] = "Treatment",
        ["stage"] = "Status",
        ["lead-source"] = "Lead source",
        ["payment-method"] = "Payment method",
        ["cancellation-reason"] = "Cancellation reason",
    };

    private const int PerTypeLimit = 500;

    // ---- Delete -------------------------------------------------------------------------------

    public async Task DeleteCustomerAsync(int id, int userId, CancellationToken ct)
    {
        var customer = await db.Customers.SingleOrDefaultAsync(c => c.Id == id, ct) ?? throw BusinessRuleException.NotFound("Customer");
        var bookings = await db.Bookings.CountAsync(b => b.CustomerId == id, ct);
        if (bookings > 0)
            throw BusinessRuleException.Conflict($"Delete {customer.Name}'s {Plural(bookings, "booking")} first.");

        MarkDeleted(customer, userId);
        audit.Record(userId, "Customer Deleted", nameof(Customer), id, new { customer.Name });
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteBookingAsync(int id, int userId, CancellationToken ct)
    {
        var booking = await db.Bookings.Include(b => b.Payments).Include(b => b.Customer).SingleOrDefaultAsync(b => b.Id == id, ct)
            ?? throw BusinessRuleException.NotFound("Booking");
        var payments = booking.Payments.Count(p => p.Status != PaymentStatus.Pending);
        if (payments > 0)
            throw BusinessRuleException.Conflict($"Delete this booking's {Plural(payments, "payment")} first.");

        MarkDeleted(booking, userId);
        // Old "nothing received yet" entries carry no money and aren't listed: they go with the booking.
        foreach (var legacy in booking.Payments) MarkDeleted(legacy, userId, booking.DeletedAt);
        audit.Record(userId, "Booking Deleted", nameof(Booking), id,
            new { customer = booking.Customer.Name, date = booking.BookingDate, status = booking.Status.ToString() });
        await db.SaveChangesAsync(ct);
    }

    public async Task DeletePaymentAsync(int id, int userId, CancellationToken ct)
    {
        var payment = await db.Payments.SingleOrDefaultAsync(p => p.Id == id && p.Status != PaymentStatus.Pending, ct)
            ?? throw BusinessRuleException.NotFound("Payment");
        var booking = await db.Bookings.Include(b => b.Payments).SingleAsync(b => b.Id == payment.BookingId, ct);

        MarkDeleted(payment, userId);
        BookingMoney.Recalculate(booking);
        audit.Record(userId, "Payment Deleted", nameof(Booking), booking.Id,
            new { amount = payment.Amount, status = payment.Status.ToString(), balance = booking.Balance });
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteLookupAsync<T>(int id, int userId, CancellationToken ct) where T : class, ILookupEntity, ISoftDeletable, new()
    {
        var item = await db.Set<T>().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw BusinessRuleException.NotFound(LookupName<T>());

        if (item is Stage { SystemKey: not null } or LeadSource { SystemKey: not null })
            throw new BusinessRuleException($"\"{item.Name}\" is built in and can't be deleted. You can rename it instead.");

        var uses = await UsesAsync(item, ct);
        if (uses.Count > 0)
            throw BusinessRuleException.Conflict(
                $"\"{item.Name}\" is still used by {string.Join(" and ", uses)}. Delete those first, or deactivate it instead.");

        MarkDeleted(item, userId);
        audit.Record(userId, $"{LookupName<T>()} Deleted", typeof(T).Name, id, new { item.Name });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Live records that point at a list item, e.g. ["3 customers", "5 bookings"].</summary>
    private async Task<List<string>> UsesAsync(ILookupEntity item, CancellationToken ct)
    {
        var id = item.Id;
        (int Customers, int Bookings, int Payments) n = item switch
        {
            Treatment => (await db.Customers.CountAsync(c => c.Treatments.Any(t => t.TreatmentId == id), ct),
                await db.Bookings.CountAsync(b => b.Treatments.Any(t => t.TreatmentId == id) || b.NextTreatmentId == id, ct), 0),
            Stage => (await db.Customers.CountAsync(c => c.StageId == id, ct), 0, 0),
            LeadSource => (await db.Customers.CountAsync(c => c.LeadSourceId == id, ct), 0, 0),
            CancellationReason => (0, await db.Bookings.CountAsync(b => b.CancellationReasonId == id, ct), 0),
            PaymentMethod => (0, 0, await db.Payments.CountAsync(p => p.PaymentMethodId == id, ct)),
            _ => (0, 0, 0),
        };
        var uses = new List<string>();
        if (n.Customers > 0) uses.Add(Plural(n.Customers, "customer"));
        if (n.Bookings > 0) uses.Add(Plural(n.Bookings, "booking"));
        if (n.Payments > 0) uses.Add(Plural(n.Payments, "payment"));
        return uses;
    }

    // ---- Restore ------------------------------------------------------------------------------

    public async Task RestoreAsync(string type, int id, int userId, CancellationToken ct)
    {
        switch (type)
        {
            case "customer": await RestoreCustomerAsync(id, userId, ct); break;
            case "booking": await RestoreBookingAsync(id, userId, ct); break;
            case "payment": await RestorePaymentAsync(id, userId, ct); break;
            case "treatment": await RestoreLookupAsync<Treatment>(id, userId, ct); break;
            case "stage": await RestoreLookupAsync<Stage>(id, userId, ct); break;
            case "lead-source": await RestoreLookupAsync<LeadSource>(id, userId, ct); break;
            case "payment-method": await RestoreLookupAsync<PaymentMethod>(id, userId, ct); break;
            case "cancellation-reason": await RestoreLookupAsync<CancellationReason>(id, userId, ct); break;
            default: throw BusinessRuleException.NotFound("Record type");
        }
    }

    private async Task RestoreCustomerAsync(int id, int userId, CancellationToken ct)
    {
        var customer = await Deleted(db.Customers).Include(c => c.Treatments).SingleOrDefaultAsync(c => c.Id == id, ct)
            ?? throw BusinessRuleException.NotFound("Deleted customer");

        await EnsureLiveAsync(db.Stages, customer.StageId, "status", ct);
        if (customer.LeadSourceId is { } source) await EnsureLiveAsync(db.LeadSources, source, "lead source", ct);
        foreach (var t in customer.Treatments) await EnsureLiveAsync(db.Treatments, t.TreatmentId, "treatment", ct);

        // Captured again while in the recycle bin: the same number can't belong to two customers.
        if (customer.WhatsAppNumber is { } number && await db.Customers.AnyAsync(c => c.WhatsAppNumber == number, ct))
            throw BusinessRuleException.Conflict($"Another customer now has the WhatsApp number of {customer.Name}, so it can't be restored.");
        if (customer.InstagramName is { } handle && await db.Customers.AnyAsync(c => c.InstagramName == handle, ct))
            throw BusinessRuleException.Conflict($"Another customer now has @{handle}, so {customer.Name} can't be restored.");

        Restore(customer);
        audit.Record(userId, "Customer Restored", nameof(Customer), id, new { customer.Name });
        await db.SaveChangesAsync(ct);
    }

    private async Task RestoreBookingAsync(int id, int userId, CancellationToken ct)
    {
        var booking = await Deleted(db.Bookings).Include(b => b.Treatments).SingleOrDefaultAsync(b => b.Id == id, ct)
            ?? throw BusinessRuleException.NotFound("Deleted booking");

        if (!await db.Customers.AnyAsync(c => c.Id == booking.CustomerId, ct))
            throw BusinessRuleException.Conflict("Restore the customer first.");
        foreach (var t in booking.Treatments) await EnsureLiveAsync(db.Treatments, t.TreatmentId, "treatment", ct);
        if (booking.NextTreatmentId is { } next) await EnsureLiveAsync(db.Treatments, next, "treatment", ct);
        if (booking.CancellationReasonId is { } reason) await EnsureLiveAsync(db.CancellationReasons, reason, "cancellation reason", ct);

        // The old "nothing received yet" entries deleted with it come back with it.
        var legacy = await db.Payments.IgnoreQueryFilters()
            .Where(p => p.BookingId == id && p.Status == PaymentStatus.Pending && p.DeletedAt == booking.DeletedAt).ToListAsync(ct);
        legacy.ForEach(Restore);
        Restore(booking);
        audit.Record(userId, "Booking Restored", nameof(Booking), id, new { date = booking.BookingDate });
        await db.SaveChangesAsync(ct);
    }

    private async Task RestorePaymentAsync(int id, int userId, CancellationToken ct)
    {
        var payment = await Deleted(db.Payments).SingleOrDefaultAsync(p => p.Id == id && p.Status != PaymentStatus.Pending, ct)
            ?? throw BusinessRuleException.NotFound("Deleted payment");
        var booking = await db.Bookings.Include(b => b.Payments).SingleOrDefaultAsync(b => b.Id == payment.BookingId, ct)
            ?? throw BusinessRuleException.Conflict("Restore the booking first.");
        if (payment.PaymentMethodId is { } method) await EnsureLiveAsync(db.PaymentMethods, method, "payment method", ct);

        // Paid again since: bringing this back would pay (or waive) more than the consultation amount.
        if (payment.Amount > booking.Balance)
            throw BusinessRuleException.Conflict(
                $"This consultation now owes {booking.Balance:#,##0.##}, less than this payment of {payment.Amount:#,##0.##}, so it can't be restored.");

        Restore(payment);
        // Usually already linked by EF (the payment was loaded first); never count it twice.
        if (!booking.Payments.Contains(payment)) booking.Payments.Add(payment);
        BookingMoney.Recalculate(booking);
        audit.Record(userId, "Payment Restored", nameof(Booking), booking.Id, new { amount = payment.Amount, balance = booking.Balance });
        await db.SaveChangesAsync(ct);
    }

    private async Task RestoreLookupAsync<T>(int id, int userId, CancellationToken ct) where T : class, ILookupEntity, ISoftDeletable, new()
    {
        var item = await Deleted(db.Set<T>()).SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw BusinessRuleException.NotFound($"Deleted {LookupName<T>().ToLower()}");
        var lower = item.Name.ToLower();
        if (await db.Set<T>().AnyAsync(x => x.Name.ToLower() == lower, ct))
            throw BusinessRuleException.Conflict($"A {LookupName<T>().ToLower()} named \"{item.Name}\" exists again. Rename that one first.");

        Restore(item);
        audit.Record(userId, $"{LookupName<T>()} Restored", typeof(T).Name, id, new { item.Name });
        await db.SaveChangesAsync(ct);
    }

    private static async Task EnsureLiveAsync<T>(DbSet<T> set, int id, string what, CancellationToken ct) where T : class, ILookupEntity
    {
        if (await set.AnyAsync(x => x.Id == id, ct)) return;
        var name = await set.IgnoreQueryFilters().Where(x => x.Id == id).Select(x => x.Name).FirstOrDefaultAsync(ct);
        throw BusinessRuleException.Conflict($"Restore the {what} \"{name}\" first.");
    }

    // ---- The recycle bin ----------------------------------------------------------------------

    public async Task<PagedResult<RecycleBinItemDto>> ListAsync(RecycleBinQuery q, CancellationToken ct)
    {
        var rows = new List<(string Type, int Id, string Title, string? Detail, DateTime DeletedAt, int? DeletedById)>();
        bool Want(string type) => string.IsNullOrEmpty(q.Type) || q.Type == type;

        if (Want("customer"))
            rows.AddRange((await Deleted(db.Customers).OrderByDescending(x => x.DeletedAt).Take(PerTypeLimit)
                    .Select(c => new { c.Id, c.Name, c.WhatsAppNumber, c.InstagramName, c.DeletedAt, c.DeletedById }).ToListAsync(ct))
                .Select(c => ("customer", c.Id, c.Name,
                    c.WhatsAppNumber is not null ? ContactNormalizer.FormatPhone(c.WhatsAppNumber) : c.InstagramName is not null ? "@" + c.InstagramName : null,
                    c.DeletedAt!.Value, c.DeletedById)));
        if (Want("booking"))
            rows.AddRange((await Deleted(db.Bookings).OrderByDescending(x => x.DeletedAt).Take(PerTypeLimit)
                    .Select(b => new
                    {
                        b.Id, Customer = b.Customer.Name, b.BookingDate, b.StartTime, b.Status, b.DeletedAt, b.DeletedById,
                        Treatments = b.Treatments.Select(t => t.Treatment.Name).ToList(),
                    }).ToListAsync(ct))
                .Select(b => ("booking", b.Id, b.Customer,
                    (string?)$"{b.BookingDate:dd MMM yyyy} {b.StartTime:HH\\:mm} · {b.Status}{(b.Treatments.Count > 0 ? " · " + string.Join(" + ", b.Treatments) : "")}",
                    b.DeletedAt!.Value, b.DeletedById)));
        if (Want("payment"))
            rows.AddRange((await Deleted(db.Payments).Where(p => p.Status != PaymentStatus.Pending)
                    .OrderByDescending(x => x.DeletedAt).Take(PerTypeLimit)
                    .Select(p => new
                    {
                        p.Id, Customer = p.Customer.Name, p.Amount, p.Status, p.Booking.BookingDate, p.DeletedAt, p.DeletedById,
                        Method = p.PaymentMethod != null ? p.PaymentMethod.Name : null,
                    }).ToListAsync(ct))
                .Select(p => ("payment", p.Id, p.Customer,
                    (string?)$"{(p.Status == PaymentStatus.Waived ? "Waived" : "Paid")} {p.Amount:#,##0.##}{(p.Method is null ? "" : " · " + p.Method)} · consultation {p.BookingDate:dd MMM yyyy}",
                    p.DeletedAt!.Value, p.DeletedById)));
        await AddLookupsAsync<Treatment>("treatment");
        await AddLookupsAsync<Stage>("stage");
        await AddLookupsAsync<LeadSource>("lead-source");
        await AddLookupsAsync<PaymentMethod>("payment-method");
        await AddLookupsAsync<CancellationReason>("cancellation-reason");

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var term = q.Search.Trim();
            rows = rows.Where(r => r.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
                                   || (r.Detail?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
        }

        var page = Math.Max(1, q.Page);
        var size = Math.Clamp(q.PageSize, 1, 200);
        var pageRows = rows.OrderByDescending(r => r.DeletedAt).Skip((page - 1) * size).Take(size).ToList();
        var userIds = pageRows.Select(r => r.DeletedById).OfType<int>().Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        var items = pageRows.Select(r => new RecycleBinItemDto(r.Type, Types[r.Type], r.Id, r.Title, r.Detail, r.DeletedAt,
            r.DeletedById is { } uid ? users.GetValueOrDefault(uid) : null)).ToList();
        return new PagedResult<RecycleBinItemDto>(items, page, size, rows.Count);

        async Task AddLookupsAsync<T>(string type) where T : class, ILookupEntity, ISoftDeletable, new()
        {
            if (!Want(type)) return;
            rows.AddRange((await Deleted(db.Set<T>()).OrderByDescending(x => x.DeletedAt).Take(PerTypeLimit)
                    .Select(x => new { x.Id, x.Name, x.DeletedAt, x.DeletedById }).ToListAsync(ct))
                .Select(x => (type, x.Id, x.Name, (string?)null, x.DeletedAt!.Value, x.DeletedById)));
        }
    }

    // ---- Helpers ------------------------------------------------------------------------------

    private static IQueryable<T> Deleted<T>(DbSet<T> set) where T : class, ISoftDeletable =>
        set.IgnoreQueryFilters().Where(x => x.DeletedAt != null);

    private static void MarkDeleted(ISoftDeletable item, int userId, DateTime? at = null)
    {
        item.DeletedAt = at ?? DateTime.UtcNow;
        item.DeletedById = userId;
    }

    private static void Restore(ISoftDeletable item)
    {
        item.DeletedAt = null;
        item.DeletedById = null;
    }

    private static string LookupName<T>() where T : class, ILookupEntity, new() => LookupService<T>.DisplayName switch
    {
        nameof(Stage) => "Status",
        var name => name,
    };

    private static string Plural(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";
}
