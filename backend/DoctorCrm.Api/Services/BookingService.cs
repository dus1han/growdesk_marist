using DoctorCrm.Api.Data;
using DoctorCrm.Api.DTOs;
using DoctorCrm.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace DoctorCrm.Api.Services;

/// <summary>
/// Consultation bookings (spec §19–§27). Only a Booked consultation can change: it can be
/// completed, rescheduled, cancelled or marked as a no-show, and every one of those is final.
/// Nothing is ever deleted.
/// </summary>
public class BookingService(AppDbContext db, AuditService audit, StageAutomation stages, ClinicClock clock)
{
    public const int MaxPageSize = 500;

    public async Task<PagedResult<BookingListItemDto>> ListAsync(BookingQuery q, CancellationToken ct)
    {
        var page = Math.Max(1, q.Page);
        var size = Math.Clamp(q.PageSize, 1, MaxPageSize);
        var query = Filter(db.Bookings.AsNoTracking(), q);

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * size).Take(size)
            .Select(b => new BookingListItemDto(
                b.Id,
                new NamedRef(b.Customer.Id, b.Customer.Name),
                b.Doctor != null ? new NamedRef(b.Doctor.Id, b.Doctor.FullName) : null,
                b.BookingDate, b.StartTime, b.EndTime,
                b.Status.ToString(),
                b.Treatments.OrderBy(t => t.Treatment.DisplayOrder).Select(t => new NamedRef(t.TreatmentId, t.Treatment.Name)).ToList(),
                b.ConsultationCharge,
                b.AmountPaid,
                b.Balance,
                BookingMoney.State(b.Status, b.ConsultationCharge, b.AmountPaid, b.Balance)))
            .AsSplitQuery()
            .ToListAsync(ct);

        return new PagedResult<BookingListItemDto>(items, page, size, total);
    }

    /// <summary>
    /// Every list filter, combined with AND and sorted. Shared by the list and the Excel export,
    /// so an export always contains exactly what the screen shows.
    /// </summary>
    public static IQueryable<Booking> Filter(IQueryable<Booking> query, BookingQuery q)
    {
        if (q.From is { } from) query = query.Where(b => b.BookingDate >= from);
        if (q.To is { } to) query = query.Where(b => b.BookingDate <= to);
        if (q.CustomerId is { } customerId) query = query.Where(b => b.CustomerId == customerId);
        if (q.DoctorId is { } doctorId) query = query.Where(b => b.DoctorId == doctorId);
        if (q.TreatmentId is { } treatmentId) query = query.Where(b => b.Treatments.Any(t => t.TreatmentId == treatmentId));
        if (!string.IsNullOrWhiteSpace(q.Status))
        {
            var statuses = q.Status.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => Enum.TryParse<BookingStatus>(s, true, out var st) ? st : (BookingStatus?)null)
                .Where(s => s is not null).Select(s => s!.Value).ToList();
            query = query.Where(b => statuses.Contains(b.Status));
        }
        query = BookingMoney.WhereState(query, q.PaymentStatus);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var term = q.Search.Trim();
            var like = $"%{term}%";
            var handle = $"%{term.TrimStart('@').ToLowerInvariant()}%";
            var digits = new string(term.Where(char.IsDigit).ToArray()).TrimStart('0');
            var digitsLike = $"%{digits}%";
            query = query.Where(b => EF.Functions.ILike(b.Customer.Name, like)
                                     || (b.Customer.InstagramName != null && EF.Functions.Like(b.Customer.InstagramName, handle))
                                     || (digits.Length >= 3 && b.Customer.WhatsAppNumber != null && EF.Functions.Like(b.Customer.WhatsAppNumber, digitsLike)));
        }

        return q.Sort == "desc"
            ? query.OrderByDescending(b => b.BookingDate).ThenByDescending(b => b.StartTime).ThenByDescending(b => b.Id)
            : query.OrderBy(b => b.BookingDate).ThenBy(b => b.StartTime).ThenBy(b => b.Id);
    }

    public async Task<BookingDetailDto> GetAsync(int id, CancellationToken ct)
    {
        var b = await db.Bookings.AsNoTracking()
            .Include(x => x.Customer).ThenInclude(c => c.Stage)
            .Include(x => x.Doctor)
            .Include(x => x.Treatments).ThenInclude(t => t.Treatment)
            .Include(x => x.NextTreatment)
            .Include(x => x.CancellationReason)
            .Include(x => x.OriginalBooking)
            .Include(x => x.Payments).ThenInclude(p => p.PaymentMethod)
            .Include(x => x.Payments).ThenInclude(p => p.CreatedBy)
            .AsSplitQuery()
            .SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw BusinessRuleException.NotFound("Booking");

        var next = await db.Bookings.AsNoTracking().Where(x => x.OriginalBookingId == id)
            .Select(x => new BookingLinkDto(x.Id, x.BookingDate, x.StartTime, x.Status.ToString())).FirstOrDefaultAsync(ct);

        return new BookingDetailDto(
            b.Id,
            new NamedRef(b.Customer.Id, b.Customer.Name),
            b.Customer.WhatsAppNumber is null ? null : ContactNormalizer.FormatPhone(b.Customer.WhatsAppNumber),
            new StageRef(b.Customer.Stage.Id, b.Customer.Stage.Name, b.Customer.Stage.Color, b.Customer.Stage.SystemKey),
            b.Doctor is null ? null : new NamedRef(b.Doctor.Id, b.Doctor.FullName),
            b.BookingDate, b.StartTime, b.EndTime, b.Status.ToString(),
            b.Treatments.OrderBy(t => t.Treatment.DisplayOrder).Select(t => new NamedRef(t.TreatmentId, t.Treatment.Name)).ToList(),
            b.Notes, b.ConsultationCharge, b.AmountPaid, b.Balance,
            BookingMoney.State(b.Status, b.ConsultationCharge, b.AmountPaid, b.Balance),
            b.DoctorNotes, b.NextTreatmentDate,
            b.NextTreatment is null ? null : new NamedRef(b.NextTreatment.Id, b.NextTreatment.Name),
            b.CancellationReason is null ? null : new NamedRef(b.CancellationReason.Id, b.CancellationReason.Name),
            b.CancellationNote,
            b.OriginalBooking is null ? null
                : new BookingLinkDto(b.OriginalBooking.Id, b.OriginalBooking.BookingDate, b.OriginalBooking.StartTime, b.OriginalBooking.Status.ToString()),
            next,
            b.Payments.Where(p => p.Status != PaymentStatus.Pending).OrderByDescending(p => p.CreatedAt).Select(p => new PaymentDto(
                p.Id, p.Amount, p.Status.ToString(),
                p.PaymentMethod is null ? null : new NamedRef(p.PaymentMethod.Id, p.PaymentMethod.Name),
                p.PaymentDate, p.CreatedBy?.FullName, p.CreatedAt)).ToList(),
            b.CompletedAt, b.CancelledAt, b.RescheduledAt, b.NoShowAt, b.CreatedAt, b.Source);
    }

    /// <param name="source">Set when made outside GrowDesk, e.g. by the WhatsApp BOT.</param>
    public async Task<BookingDetailDto> CreateAsync(CreateBookingRequest r, int? userId, CancellationToken ct, string? source = null)
    {
        var customer = await db.Customers.SingleOrDefaultAsync(c => c.Id == r.CustomerId && c.IsActive, ct)
            ?? throw new BusinessRuleException("Choose a customer.", field: "customerId");

        await EnsureDoctorAsync(r.DoctorId, null, ct);
        // Staff always choose a treatment; the WhatsApp BOT may not know it yet (chosen when completing).
        var treatmentIds = await ValidateTreatmentsAsync(r.TreatmentIds, new HashSet<int>(), ct, allowEmpty: source is not null);
        await EnsureNotBlockedAsync(r.Date, r.StartTime, r.EndTime, ct);
        await EnsureNoOverlapAsync(r.Date, r.StartTime, r.EndTime, r.DoctorId, excludeId: null, ct);

        var booking = new Booking
        {
            CustomerId = customer.Id,
            DoctorId = r.DoctorId,
            BookingDate = r.Date,
            StartTime = r.StartTime,
            EndTime = r.EndTime,
            Notes = Clean(r.Notes),
            CreatedById = userId,
            Source = source,
        };
        foreach (var id in treatmentIds) booking.Treatments.Add(new BookingTreatment { TreatmentId = id });
        db.Bookings.Add(booking);

        await db.SaveChangesAsync(ct);
        audit.Record(userId, "Booking Created", nameof(Booking), booking.Id,
            new { customerId = customer.Id, date = r.Date, start = r.StartTime, source });
        await db.SaveChangesAsync(ct);
        return await GetAsync(booking.Id, ct);
    }

    public async Task<BookingDetailDto> UpdateAsync(int id, UpdateBookingRequest r, int? userId, CancellationToken ct)
    {
        var booking = await LoadBookedAsync(id, "edited", ct);
        await EnsureDoctorAsync(r.DoctorId, booking.DoctorId, ct);

        var current = booking.Treatments.Select(t => t.TreatmentId).ToHashSet();
        var treatmentIds = await ValidateTreatmentsAsync(r.TreatmentIds, current, ct, allowEmpty: current.Count == 0);

        if (r.DoctorId != booking.DoctorId)
            await EnsureNoOverlapAsync(booking.BookingDate, booking.StartTime, booking.EndTime, r.DoctorId, booking.Id, ct);

        booking.DoctorId = r.DoctorId;
        booking.Notes = Clean(r.Notes);
        foreach (var removed in booking.Treatments.Where(t => !treatmentIds.Contains(t.TreatmentId)).ToList())
            booking.Treatments.Remove(removed);
        foreach (var added in treatmentIds.Where(t => !current.Contains(t)))
            booking.Treatments.Add(new BookingTreatment { TreatmentId = added });

        audit.Record(userId, "Booking Updated", nameof(Booking), id, new { treatments = treatmentIds, r.DoctorId });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<BookingDetailDto> CompleteAsync(int id, CompleteBookingRequest r, int? userId, CancellationToken ct)
    {
        var booking = await LoadBookedAsync(id, "completed", ct);

        // A booking made without a treatment gets one now: what was actually done. It joins the
        // customer's interests too.
        if (booking.Treatments.Count == 0)
        {
            if (r.TreatmentIds is not { Count: > 0 })
                throw new BusinessRuleException("Choose the treatment for this consultation.", field: "treatmentIds");
            var chosen = await ValidateTreatmentsAsync(r.TreatmentIds, new HashSet<int>(), ct);
            foreach (var tid in chosen) booking.Treatments.Add(new BookingTreatment { TreatmentId = tid });
            var customer = await db.Customers.Include(c => c.Treatments).SingleAsync(c => c.Id == booking.CustomerId, ct);
            var added = chosen.Where(tid => customer.Treatments.All(t => t.TreatmentId != tid)).ToList();
            foreach (var tid in added) customer.Treatments.Add(new CustomerTreatment { TreatmentId = tid, CreatedAt = DateTime.UtcNow });
            if (added.Count > 0)
            {
                var names = await db.Treatments.Where(t => added.Contains(t.Id)).Select(t => t.Name).ToListAsync(ct);
                audit.Record(userId, "Treatment Added", nameof(Customer), customer.Id, new { treatments = names });
            }
        }

        // Paid now: nothing up to the full amount. The rest is the balance, paid later or waived.
        if (r.PaidAmount < 0 || r.PaidAmount > r.ConsultationCharge)
            throw new BusinessRuleException("The paid amount must be between 0 and the consultation amount.", field: "paidAmount");
        if (r.PaidAmount > 0 && r.PaymentMethodId is null)
            throw new BusinessRuleException("Choose how the customer paid.", field: "paymentMethodId");
        if (r.PaidAmount > 0 && !await db.PaymentMethods.AnyAsync(x => x.Id == r.PaymentMethodId && x.IsActive, ct))
            throw new BusinessRuleException("Choose a valid payment method.", field: "paymentMethodId");

        // Next treatment: both or neither (spec §24). The validator checks too; this is the backstop.
        if ((r.NextTreatmentDate is null) != (r.NextTreatmentId is null))
            throw new BusinessRuleException("Enter both the next treatment and its date, or leave both empty.",
                field: r.NextTreatmentDate is null ? "nextTreatmentDate" : "nextTreatmentId");
        if (r.NextTreatmentId is { } nt && !await db.Treatments.AnyAsync(t => t.Id == nt && t.IsActive, ct))
            throw new BusinessRuleException("Choose a valid treatment.", field: "nextTreatmentId");
        if (r.NextTreatmentDate is { } nd && nd < booking.BookingDate)
            throw new BusinessRuleException("The next treatment can't be before this consultation.", field: "nextTreatmentDate");

        var now = DateTime.UtcNow;
        booking.Status = BookingStatus.Completed;
        booking.CompletedAt = now;
        booking.ConsultationCharge = r.ConsultationCharge;
        booking.DoctorNotes = Clean(r.DoctorNotes);
        booking.NextTreatmentDate = r.NextTreatmentDate;
        booking.NextTreatmentId = r.NextTreatmentId;

        if (r.PaidAmount > 0)
            booking.Payments.Add(new Payment
            {
                CustomerId = booking.CustomerId,
                Amount = r.PaidAmount,
                Status = PaymentStatus.Paid,
                PaymentMethodId = r.PaymentMethodId,
                PaymentDate = now,
                CreatedById = userId,
                CreatedAt = now,
            });
        BookingMoney.Recalculate(booking);

        // The one automatic status change: Interested / Follow-up / Lost → Customer.
        await stages.MoveAsync(booking.Customer, [StageKeys.Interested, StageKeys.FollowUp, StageKeys.Lost], StageKeys.Customer, userId, "Consultation completed", ct);

        audit.Record(userId, "Consultation Completed", nameof(Booking), id,
            new { charge = r.ConsultationCharge, paid = r.PaidAmount, balance = booking.Balance, nextTreatment = r.NextTreatmentDate });
        if (r.PaidAmount > 0)
            audit.Record(userId, "Payment Recorded", nameof(Booking), id, new { amount = r.PaidAmount, balance = booking.Balance });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>
    /// The original becomes Rescheduled with no charge; a new Booked consultation keeps the
    /// customer, treatments and doctor and points back via OriginalBookingId (spec §25).
    /// </summary>
    public async Task<BookingDetailDto> RescheduleAsync(int id, RescheduleBookingRequest r, int? userId, CancellationToken ct, string? source = null)
    {
        var original = await LoadBookedAsync(id, "rescheduled", ct);
        var doctorId = r.DoctorId ?? original.DoctorId;
        await EnsureDoctorAsync(doctorId, original.DoctorId, ct);
        await EnsureNotBlockedAsync(r.Date, r.StartTime, r.EndTime, ct);
        await EnsureNoOverlapAsync(r.Date, r.StartTime, r.EndTime, doctorId, excludeId: original.Id, ct);

        original.Status = BookingStatus.Rescheduled;
        original.RescheduledAt = DateTime.UtcNow;
        original.ConsultationCharge = 0;

        var replacement = new Booking
        {
            CustomerId = original.CustomerId,
            DoctorId = doctorId,
            BookingDate = r.Date,
            StartTime = r.StartTime,
            EndTime = r.EndTime,
            Notes = original.Notes,
            OriginalBookingId = original.Id,
            CreatedById = userId,
            Source = source,
        };
        foreach (var t in original.Treatments) replacement.Treatments.Add(new BookingTreatment { TreatmentId = t.TreatmentId });
        db.Bookings.Add(replacement);
        await db.SaveChangesAsync(ct);

        audit.Record(userId, "Booking Rescheduled", nameof(Booking), original.Id,
            new { from = new { date = original.BookingDate, start = original.StartTime }, to = new { date = r.Date, start = r.StartTime }, newBookingId = replacement.Id, source });
        await db.SaveChangesAsync(ct);
        return await GetAsync(replacement.Id, ct);
    }

    public async Task<BookingDetailDto> CancelAsync(int id, CancelBookingRequest r, int? userId, CancellationToken ct)
    {
        var booking = await LoadBookedAsync(id, "cancelled", ct);
        var reason = await db.CancellationReasons.SingleOrDefaultAsync(x => x.Id == r.CancellationReasonId && x.IsActive, ct)
            ?? throw new BusinessRuleException("Choose a cancellation reason.", field: "cancellationReasonId");

        booking.Status = BookingStatus.Cancelled;
        booking.CancelledAt = DateTime.UtcNow;
        booking.CancellationReasonId = reason.Id;
        booking.CancellationNote = Clean(r.Note);

        audit.Record(userId, "Booking Cancelled", nameof(Booking), id, new { reason = reason.Name });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<BookingDetailDto> MarkNoShowAsync(int id, int? userId, CancellationToken ct)
    {
        var booking = await LoadBookedAsync(id, "marked as a no-show", ct);
        if (booking.BookingDate > await clock.TodayAsync(ct))
            throw new BusinessRuleException("A booking can't be a no-show before its date.");

        booking.Status = BookingStatus.NoShow;
        booking.NoShowAt = DateTime.UtcNow;
        audit.Record(userId, "No Show", nameof(Booking), id);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>The single gate for status changes: only Booked consultations can move.</summary>
    private async Task<Booking> LoadBookedAsync(int id, string action, CancellationToken ct)
    {
        var booking = await db.Bookings
            .Include(b => b.Treatments)
            .Include(b => b.Customer)
            .SingleOrDefaultAsync(b => b.Id == id, ct)
            ?? throw BusinessRuleException.NotFound("Booking");

        if (booking.Status != BookingStatus.Booked)
            throw new BusinessRuleException(
                $"This booking is already {Describe(booking.Status)}, so it can't be {action}.", StatusCodes.Status409Conflict);
        return booking;
    }

    private async Task EnsureNoOverlapAsync(DateOnly date, TimeOnly start, TimeOnly end, int? doctorId, int? excludeId, CancellationToken ct)
    {
        var conflict = await db.Bookings.AsNoTracking()
            .Where(b => b.Status == BookingStatus.Booked && b.BookingDate == date && b.DoctorId == doctorId && b.Id != excludeId
                        && b.StartTime < end && start < b.EndTime)
            .OrderBy(b => b.StartTime)
            .Select(b => new BookingConflictDto(b.Id, b.Customer.Name, b.StartTime, b.EndTime))
            .FirstOrDefaultAsync(ct);

        if (conflict is not null)
            throw new BusinessRuleException(
                $"This time overlaps {conflict.CustomerName}'s consultation ({conflict.StartTime:HH\\:mm}–{conflict.EndTime:HH\\:mm}).",
                StatusCodes.Status409Conflict, "startTime") { Details = conflict };
    }

    /// <summary>Time marked as not available on the calendar can't be booked by anyone.</summary>
    private async Task EnsureNotBlockedAsync(DateOnly date, TimeOnly start, TimeOnly end, CancellationToken ct)
    {
        var block = await CalendarBlockService.Covering(db.CalendarBlocks.AsNoTracking(), date, start, end).FirstOrDefaultAsync(ct);
        if (block is not null)
            throw new BusinessRuleException(
                $"This time is {CalendarBlockService.Describe(block)}. Choose another time, or remove the block on the calendar first.",
                StatusCodes.Status409Conflict, "startTime");
    }

    private async Task EnsureDoctorAsync(int? doctorId, int? currentDoctorId, CancellationToken ct)
    {
        if (doctorId is { } d && d != currentDoctorId && !await db.Users.AnyAsync(u => u.Id == d && u.IsActive, ct))
            throw new BusinessRuleException("Choose an active doctor.", field: "doctorId");
    }

    private async Task<List<int>> ValidateTreatmentsAsync(IReadOnlyList<int> requested, IReadOnlySet<int> current, CancellationToken ct, bool allowEmpty = false)
    {
        var ids = requested.Distinct().ToList();
        if (ids.Count == 0 && allowEmpty) return ids;
        if (ids.Count == 0) throw new BusinessRuleException("Choose at least one treatment.", field: "treatmentIds");
        var valid = await db.Treatments.CountAsync(t => ids.Contains(t.Id) && (t.IsActive || current.Contains(t.Id)), ct);
        if (valid != ids.Count) throw new BusinessRuleException("One of the selected treatments is no longer available.", field: "treatmentIds");
        return ids;
    }

    private static string Describe(BookingStatus s) => s switch
    {
        BookingStatus.Completed => "completed",
        BookingStatus.Rescheduled => "rescheduled",
        BookingStatus.Cancelled => "cancelled",
        BookingStatus.NoShow => "marked as a no-show",
        _ => "booked",
    };

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
