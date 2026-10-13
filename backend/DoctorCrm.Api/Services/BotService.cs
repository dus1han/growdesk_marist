using System.Globalization;
using DoctorCrm.Api.Data;
using DoctorCrm.Api.DTOs;
using DoctorCrm.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace DoctorCrm.Api.Services;

/// <summary>
/// The WhatsApp BOT API. The bot owns no business rules: the bot saves each new contact as an
/// interested customer, then books for them; GrowDesk finds the customer by WhatsApp number,
/// works out the end time, keeps bookings inside opening hours and free of clashes, and
/// moves stages. Bookings go on the shared calendar (no doctor) and are marked "WhatsApp BOT".
/// </summary>
public class BotService(
    AppDbContext db,
    AuditService audit,
    ContactNormalizer contacts,
    ClinicClock clock,
    BookingService bookings,
    BookingHoursService hours,
    LiveEvents live,
    ILogger<BotService> logger)
{
    /// <summary>Lead source of customers the bot creates, and the source of its bookings.</summary>
    public const string SourceName = "WhatsApp BOT";

    /// <summary>Free times are offered every 15 minutes.</summary>
    public const int SlotStepMinutes = 15;

    /// <summary>How far ahead the bot may look and book.</summary>
    public const int MaxDaysAhead = 180;

    // ---- Reading ---------------------------------------------------------------------------------

    public async Task<IReadOnlyList<BotTreatmentDto>> TreatmentsAsync(CancellationToken ct) =>
        await db.Treatments.AsNoTracking().Where(t => t.IsActive).OrderBy(t => t.DisplayOrder).ThenBy(t => t.Name)
            .Select(t => new BotTreatmentDto(t.Id, t.Name, t.Description)).ToListAsync(ct);

    public async Task<BotAvailabilityDto> AvailabilityAsync(string? date, CancellationToken ct)
    {
        var day = ParseDate(date);
        await EnsureBookableDayAsync(day, ct);
        var h = await hours.GetAsync(ct);
        if (BookingHoursService.OpenOn(h, day) is not { } open)
            return new BotAvailabilityDto(Format(day), false, null, null, h.BotBookingMinutes, []);

        var free = await FreeTimesAsync(day, h.BotBookingMinutes, open, excludeId: null, ct);
        return new BotAvailabilityDto(Format(day), true, Format(open.From), Format(open.To), h.BotBookingMinutes, free.Select(Format).ToList());
    }

    /// <summary>The customer's upcoming booked consultations, soonest first. Unknown numbers get an empty list.</summary>
    public async Task<IReadOnlyList<BotBookingDto>> UpcomingAsync(string? whatsApp, CancellationToken ct)
    {
        var number = Phone(whatsApp);
        var today = await clock.TodayAsync(ct);
        var ids = await db.Bookings.AsNoTracking()
            .Where(b => b.Customer.WhatsAppNumber == number && b.Status == BookingStatus.Booked && b.BookingDate >= today)
            .OrderBy(b => b.BookingDate).ThenBy(b => b.StartTime)
            .Select(b => b.Id).Take(20).ToListAsync(ct);
        var list = new List<BotBookingDto>();
        foreach (var id in ids) list.Add(await ToBotAsync(id, ct));
        return list;
    }

    // ---- Leads --------------------------------------------------------------------------------------

    public async Task<BotLeadResultDto> LeadAsync(BotLeadRequest r, string client, CancellationToken ct)
    {
        var name = Name(r.Name);
        var number = Phone(r.WhatsApp);
        var treatmentIds = await TreatmentIdsAsync(r.TreatmentIds, ct, allowEmpty: true);
        var notes = Notes(r.Notes);

        var (customer, action) = await UpsertCustomerAsync(name, number, treatmentIds, notes, client, ct);
        await SaveAsync(ct);
        return new BotLeadResultDto(customer.Id, action, customer.Name);
    }

    // ---- Bookings ------------------------------------------------------------------------------------

    /// <summary>
    /// Books a consultation for a customer the bot already saved (POST /customers on their first
    /// message). An unknown number answers 404, so the bot saves the customer first.
    /// </summary>
    public async Task<BotBookingResultDto> BookAsync(BotBookingRequest r, string client, CancellationToken ct)
    {
        var number = Phone(r.WhatsApp);
        // Empty when the bot couldn't tell: the treatment is then chosen when the consultation is completed.
        var treatmentIds = await TreatmentIdsAsync(r.TreatmentIds, ct, allowEmpty: true);
        var notes = Notes(r.Notes);
        var (day, start, end) = await SlotAsync(r.Date, r.StartTime, ct);

        var customer = await db.Customers.Include(c => c.Treatments).SingleOrDefaultAsync(c => c.WhatsAppNumber == number, ct)
            ?? throw new BusinessRuleException(
                "No customer has this WhatsApp number yet. Save them with POST /api/bot/customers first.",
                StatusCodes.Status404NotFound, "whatsapp");

        BookingDetailDto created;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await LockDayAsync(day, ct);
            await EnsureFreeAsync(day, start, end, excludeId: null, ct);

            // The booked treatments join the customer's interests.
            await UpdateKnownAsync(customer, treatmentIds, null, client, ct);
            await SaveAsync(ct);

            created = await bookings.CreateAsync(
                new CreateBookingRequest(customer.Id, null, day, start, end, treatmentIds, notes), null, ct, SourceName);
            await tx.CommitAsync(ct);
        }

        await PublishAsync(created.Id, null, ct);
        return new BotBookingResultDto("booked", await ToBotAsync(created.Id, ct), null);
    }

    public async Task<BotBookingResultDto> UpdateAsync(int bookingId, BotBookingUpdateRequest r, string client, CancellationToken ct)
    {
        var number = Phone(r.WhatsApp);
        var booking = await db.Bookings.AsNoTracking().Include(b => b.Customer).Include(b => b.Treatments)
            .SingleOrDefaultAsync(b => b.Id == bookingId, ct);
        // Someone else's booking looks the same as a missing one, so IDs can't be probed.
        if (booking is null || booking.Customer.WhatsAppNumber != number)
            throw BusinessRuleException.NotFound("Booking");
        if (booking.Status != BookingStatus.Booked)
            throw new BusinessRuleException($"This booking is already {booking.Status.ToString().ToLowerInvariant()}, so it can't be changed.", StatusCodes.Status409Conflict);

        var moving = r.Date is not null || r.StartTime is not null;
        if (moving && (string.IsNullOrWhiteSpace(r.Date) || string.IsNullOrWhiteSpace(r.StartTime)))
            throw new BusinessRuleException("Send both date and startTime to move a booking.", field: string.IsNullOrWhiteSpace(r.Date) ? "date" : "startTime");
        var editing = r.TreatmentIds is not null || r.Notes is not null;
        if (!moving && !editing)
            throw new BusinessRuleException("Nothing to change: send a new date and startTime, treatmentIds or notes.");

        var treatmentIds = r.TreatmentIds is null ? null : await TreatmentIdsAsync(r.TreatmentIds, ct);
        var notes = r.Notes is null ? booking.Notes : Notes(r.Notes);
        (DateOnly Day, TimeOnly Start, TimeOnly End)? slot = moving ? await SlotAsync(r.Date, r.StartTime, ct) : null;

        var resultId = bookingId;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            if (slot is { } s)
            {
                await LockDayAsync(s.Day, ct);
                await EnsureFreeAsync(s.Day, s.Start, s.End, excludeId: bookingId, ct);
            }
            if (editing)
            {
                await bookings.UpdateAsync(bookingId, new UpdateBookingRequest(
                    booking.DoctorId, treatmentIds ?? booking.Treatments.Select(t => t.TreatmentId).ToList(), notes), null, ct);
            }
            if (slot is { } m)
            {
                var moved = await bookings.RescheduleAsync(bookingId, new RescheduleBookingRequest(m.Day, m.Start, m.End, null), null, ct, SourceName);
                resultId = moved.Id;
            }
            audit.Record(null, "Bot Booking Change", nameof(Booking), resultId, new { source = SourceName, client, bookingId, moved = moving, edited = editing });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        await PublishAsync(resultId, moving ? null : "booking.updated", ct);
        return new BotBookingResultDto(moving ? "rescheduled" : "updated", await ToBotAsync(resultId, ct), moving ? bookingId : null);
    }

    /// <summary>
    /// The customer cancels through the bot. The booking is kept as Cancelled (never deleted), with
    /// the reason "Customer request" and a note saying it came through the bot.
    /// </summary>
    public async Task<BotBookingResultDto> CancelAsync(int bookingId, BotCancelRequest r, string client, CancellationToken ct)
    {
        var number = Phone(r.WhatsApp);
        var note = r.Note?.Trim();
        if (note?.Length > 500) throw new BusinessRuleException("The note can be at most 500 characters.", field: "note");

        var booking = await db.Bookings.AsNoTracking().Include(b => b.Customer).SingleOrDefaultAsync(b => b.Id == bookingId, ct);
        // Someone else's booking looks the same as a missing one, so IDs can't be probed.
        if (booking is null || booking.Customer.WhatsAppNumber != number)
            throw BusinessRuleException.NotFound("Booking");
        if (booking.Status != BookingStatus.Booked)
            throw new BusinessRuleException($"This booking is already {booking.Status.ToString().ToLowerInvariant()}, so it can't be cancelled.", StatusCodes.Status409Conflict);

        var reasonId = await CancellationReasonIdAsync(ct);
        await bookings.CancelAsync(bookingId, new CancelBookingRequest(reasonId,
            string.IsNullOrEmpty(note) ? $"Cancelled by the customer through the {SourceName}." : $"{note} (via {SourceName})"), null, ct);
        audit.Record(null, "Bot Booking Change", nameof(Booking), bookingId, new { source = SourceName, client, cancelled = true });
        await db.SaveChangesAsync(ct);

        await PublishAsync(bookingId, "booking.cancelled", ct);
        return new BotBookingResultDto("cancelled", await ToBotAsync(bookingId, ct), null);
    }

    /// <summary>"Customer request" if the clinic has it, else their first active reason; added if there is none.</summary>
    private async Task<int> CancellationReasonIdAsync(CancellationToken ct)
    {
        var active = await db.CancellationReasons.Where(x => x.IsActive).OrderBy(x => x.DisplayOrder).Select(x => new { x.Id, x.Name }).ToListAsync(ct);
        var match = active.FirstOrDefault(x => string.Equals(x.Name, "Customer request", StringComparison.OrdinalIgnoreCase)) ?? active.FirstOrDefault();
        if (match is not null) return match.Id;
        var reason = new CancellationReason { Name = "Customer request", IsActive = true, DisplayOrder = 1 };
        db.CancellationReasons.Add(reason);
        await db.SaveChangesAsync(ct);
        return reason.Id;
    }

    // ---- Live updates ----------------------------------------------------------------------------------

    /// <summary>
    /// Bot bookings as live events: "booking.created", or "booking.rescheduled" for a booking
    /// that replaced another. Shared with the live stream's catch-up after a reconnect.
    /// </summary>
    public static Task<List<LiveBookingEventDto>> LiveEventsAsync(IQueryable<Booking> query, CancellationToken ct) =>
        query.OrderBy(b => b.Id).Select(b => new LiveBookingEventDto(
                b.OriginalBookingId != null ? "booking.rescheduled" : "booking.created",
                b.Id, b.CustomerId, b.Customer.Name,
                b.OriginalBookingId == null && b.CreatedAt - b.Customer.CreatedAt < TimeSpan.FromMinutes(2),
                b.BookingDate, b.StartTime, b.EndTime,
                b.Treatments.OrderBy(t => t.Treatment.DisplayOrder).Select(t => t.Treatment.Name).ToList(),
                b.Source ?? SourceName, b.CreatedAt))
            .ToListAsync(ct);

    /// <summary>Tells open GrowDesk screens. A failure here must never fail the bot's request: the booking is saved.</summary>
    private async Task PublishAsync(int bookingId, string? type, CancellationToken ct)
    {
        try
        {
            var events = await LiveEventsAsync(db.Bookings.AsNoTracking().Where(b => b.Id == bookingId), ct);
            foreach (var e in events) live.Publish(type is null ? e : e with { Type = type });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not publish the live event for booking {BookingId}", bookingId);
        }
    }

    // ---- Customers ---------------------------------------------------------------------------------------

    /// <summary>
    /// Finds the customer by WhatsApp number or creates them (Interested, source WhatsApp BOT).
    /// A known customer keeps their name, stage and source; new treatments are added and notes appended.
    /// </summary>
    private async Task<(Customer Customer, string Action)> UpsertCustomerAsync(
        string name, string number, IReadOnlyList<int> treatmentIds, string? notes, string client, CancellationToken ct)
    {
        var customer = await db.Customers.Include(c => c.Treatments).SingleOrDefaultAsync(c => c.WhatsAppNumber == number, ct);
        if (customer is null)
        {
            var today = await clock.TodayAsync(ct);
            customer = new Customer
            {
                Name = name,
                WhatsAppNumber = number,
                StageId = await db.Stages.Where(s => s.SystemKey == StageKeys.Interested).Select(s => s.Id).SingleAsync(ct),
                LeadSourceId = await SourceIdAsync(ct),
                Notes = notes,
                LastContactDate = today,
                NextFollowUpDate = today, // followed up the same day, unless the bot books them now
            };
            foreach (var id in treatmentIds)
                customer.Treatments.Add(new CustomerTreatment { TreatmentId = id, CreatedAt = DateTime.UtcNow });
            db.Customers.Add(customer);
            await SaveAsync(ct);
            audit.Record(null, "Customer Created", nameof(Customer), customer.Id, new { customer.Name, source = SourceName, client });
            return (customer, "created");
        }

        await UpdateKnownAsync(customer, treatmentIds, notes, client, ct);
        return (customer, "updated");
    }

    /// <summary>
    /// A known customer heard from again: reactivated if archived, the bot's source if none was
    /// recorded, new treatments added and notes appended. Name and stage stay as they are.
    /// </summary>
    private async Task UpdateKnownAsync(Customer customer, IReadOnlyList<int> treatmentIds, string? notes, string client, CancellationToken ct)
    {
        var today = await clock.TodayAsync(ct);
        var changed = new List<string>();
        if (!customer.IsActive)
        {
            customer.IsActive = true;
            changed.Add(nameof(Customer.IsActive));
        }
        if (customer.LeadSourceId is null)
        {
            customer.LeadSourceId = await SourceIdAsync(ct);
            changed.Add(nameof(Customer.LeadSourceId));
        }
        if (notes is not null && customer.Notes?.Contains(notes, StringComparison.Ordinal) != true)
        {
            customer.Notes = string.IsNullOrEmpty(customer.Notes) ? notes : $"{customer.Notes}\n\n{notes}";
            changed.Add(nameof(Customer.Notes));
        }

        var known = customer.Treatments.Select(t => t.TreatmentId).ToHashSet();
        var added = treatmentIds.Where(id => !known.Contains(id)).ToList();
        foreach (var id in added)
            customer.Treatments.Add(new CustomerTreatment { TreatmentId = id, CreatedAt = DateTime.UtcNow });
        if (added.Count > 0)
        {
            var names = await db.Treatments.Where(t => added.Contains(t.Id)).Select(t => t.Name).ToListAsync(ct);
            audit.Record(null, "Treatment Added", nameof(Customer), customer.Id, new { treatments = names, source = SourceName, client });
        }

        // Enquired again with nothing pending and nothing booked: back into today's follow-ups.
        if (customer.NextFollowUpDate is null && !await db.Bookings.AnyAsync(b => b.CustomerId == customer.Id
                && b.Status == BookingStatus.Booked && b.BookingDate >= today, ct))
            FollowUpService.Start(customer, today);
        customer.LastContactDate = today;
        if (changed.Count > 0)
            audit.Record(null, "Customer Updated", nameof(Customer), customer.Id, new { fields = changed, source = SourceName, client });
    }

    /// <summary>The "WhatsApp BOT" lead source; added if an admin removed or renamed it.</summary>
    private async Task<int> SourceIdAsync(CancellationToken ct)
    {
        var id = await db.LeadSources.Where(s => s.Name == SourceName).Select(s => (int?)s.Id).FirstOrDefaultAsync(ct);
        if (id is { } existing) return existing;
        var order = await db.LeadSources.MaxAsync(s => (int?)s.DisplayOrder, ct) ?? 0;
        var source = new LeadSource { Name = SourceName, IsActive = true, DisplayOrder = order + 1 };
        db.LeadSources.Add(source);
        await db.SaveChangesAsync(ct);
        return source.Id;
    }

    /// <summary>Two messages for the same new number at once: the second gets a clear retry message.</summary>
    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            throw BusinessRuleException.Conflict("This customer was just saved by another request. Please send it again.");
        }
    }

    // ---- Times -------------------------------------------------------------------------------------------

    /// <summary>Parses and checks a requested start: open that day, inside opening hours, not in the past.</summary>
    private async Task<(DateOnly Day, TimeOnly Start, TimeOnly End)> SlotAsync(string? date, string? startTime, CancellationToken ct)
    {
        var day = ParseDate(date);
        var start = BookingHoursService.ParseTime(startTime)
            ?? throw new BusinessRuleException("Send startTime as HH:mm, e.g. 16:00.", field: "startTime");
        await EnsureBookableDayAsync(day, ct);

        var h = await hours.GetAsync(ct);
        var endSpan = start.ToTimeSpan() + TimeSpan.FromMinutes(h.BotBookingMinutes);
        if (BookingHoursService.OpenOn(h, day) is not { } open)
            throw new BusinessRuleException($"The clinic is closed on {day.DayOfWeek}s.", field: "date");
        if (start < open.From || endSpan > open.To.ToTimeSpan())
            throw new BusinessRuleException(
                $"On {day.DayOfWeek}s a {h.BotBookingMinutes}-minute consultation must start between {Format(open.From)} and {Format(open.To.Add(-TimeSpan.FromMinutes(h.BotBookingMinutes)))}.",
                field: "startTime");

        if (day == await clock.TodayAsync(ct) && start <= await NowAsync(ct))
            throw new BusinessRuleException("That time has already passed today.", field: "startTime");
        return (day, start, TimeOnly.FromTimeSpan(endSpan));
    }

    private async Task EnsureBookableDayAsync(DateOnly day, CancellationToken ct)
    {
        var today = await clock.TodayAsync(ct);
        if (day < today) throw new BusinessRuleException("That date has already passed.", field: "date");
        if (day > today.AddDays(MaxDaysAhead))
            throw new BusinessRuleException($"Bookings can be made up to {MaxDaysAhead} days ahead.", field: "date");
    }

    /// <summary>A clash with a booking or blocked time answers 409 with the other free times that day, so the bot can offer them.</summary>
    private async Task EnsureFreeAsync(DateOnly day, TimeOnly start, TimeOnly end, int? excludeId, CancellationToken ct)
    {
        var taken = await db.Bookings.AsNoTracking().AnyAsync(b => b.Status == BookingStatus.Booked && b.DoctorId == null
                && b.BookingDate == day && b.Id != excludeId && b.StartTime < end && start < b.EndTime, ct)
            || await CalendarBlockService.Covering(db.CalendarBlocks, day, start, end).AnyAsync(ct);
        if (!taken) return;

        var h = await hours.GetAsync(ct);
        var free = BookingHoursService.OpenOn(h, day) is { } open
            ? await FreeTimesAsync(day, h.BotBookingMinutes, open, excludeId, ct)
            : [];
        throw new BusinessRuleException($"{Format(start)} on {Format(day)} is not available. Choose another time.", StatusCodes.Status409Conflict, "startTime")
        {
            Details = new BotSlotTakenDto(Format(day), Format(start), free.Select(Format).ToList()),
        };
    }

    private async Task<List<TimeOnly>> FreeTimesAsync(DateOnly day, int minutes, (TimeOnly From, TimeOnly To) open, int? excludeId, CancellationToken ct)
    {
        var booked = await db.Bookings.AsNoTracking()
            .Where(b => b.Status == BookingStatus.Booked && b.DoctorId == null && b.BookingDate == day && b.Id != excludeId)
            .Select(b => new { b.StartTime, b.EndTime }).ToListAsync(ct);
        var blocked = await CalendarBlockService.OnDayAsync(db, day, ct);
        TimeOnly? after = day == await clock.TodayAsync(ct) ? await NowAsync(ct) : null;

        var length = TimeSpan.FromMinutes(minutes);
        var free = new List<TimeOnly>();
        for (var t = open.From.ToTimeSpan(); t + length <= open.To.ToTimeSpan(); t += TimeSpan.FromMinutes(SlotStepMinutes))
        {
            var start = TimeOnly.FromTimeSpan(t);
            var end = TimeOnly.FromTimeSpan(t + length);
            if (after is { } now && start <= now) continue;
            if (booked.Any(b => b.StartTime < end && start < b.EndTime)) continue;
            if (blocked.Any(b => b.Start < end && start < b.End)) continue;
            free.Add(start);
        }
        return free;
    }

    /// <summary>
    /// Serialises bookings for one day (transaction-scoped Postgres advisory lock), so two
    /// requests for the same time can't both pass the clash check.
    /// </summary>
    private Task LockDayAsync(DateOnly day, CancellationToken ct)
    {
        long key = 0x6764_0000_0000L + day.DayNumber; // "gd" prefix keeps it apart from any other lock
        return db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({key})", ct);
    }

    private async Task<TimeOnly> NowAsync(CancellationToken ct) =>
        TimeOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, await clock.ZoneAsync(ct)));

    // ---- Input -------------------------------------------------------------------------------------------

    private static string Name(string? value)
    {
        var name = value?.Trim();
        if (string.IsNullOrEmpty(name)) throw new BusinessRuleException("Send the customer's name.", field: "name");
        if (name.Length > 150) throw new BusinessRuleException("The name can be at most 150 characters.", field: "name");
        return name;
    }

    private string Phone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new BusinessRuleException("Send the customer's WhatsApp number.", field: "whatsapp");
        return contacts.NormalizePhone(value)
            ?? throw new BusinessRuleException("The WhatsApp number isn't valid. Send it with the country code, e.g. +971501234567.", field: "whatsapp");
    }

    private async Task<List<int>> TreatmentIdsAsync(IReadOnlyList<int>? requested, CancellationToken ct, bool allowEmpty = false)
    {
        var ids = requested?.Distinct().ToList() ?? [];
        if (ids.Count == 0 && allowEmpty) return ids;
        if (ids.Count == 0) throw new BusinessRuleException("Send at least one treatment ID (see GET /api/bot/treatments).", field: "treatmentIds");
        var active = await db.Treatments.Where(t => ids.Contains(t.Id) && t.IsActive).Select(t => t.Id).ToListAsync(ct);
        var unknown = ids.Except(active).ToList();
        if (unknown.Count > 0)
            throw new BusinessRuleException($"Treatment {string.Join(", ", unknown)} is not available. Use an ID from GET /api/bot/treatments.", field: "treatmentIds");
        return ids;
    }

    private static string? Notes(string? value)
    {
        var notes = value?.Trim();
        if (string.IsNullOrEmpty(notes)) return null;
        if (notes.Length > 2000) throw new BusinessRuleException("Notes can be at most 2000 characters.", field: "notes");
        return notes;
    }

    private static DateOnly ParseDate(string? value) =>
        DateOnly.TryParseExact(value?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : throw new BusinessRuleException("Send the date as yyyy-MM-dd, e.g. 2026-10-05.", field: "date");

    private async Task<BotBookingDto> ToBotAsync(int bookingId, CancellationToken ct)
    {
        var b = await db.Bookings.AsNoTracking()
            .Where(x => x.Id == bookingId)
            .Select(x => new
            {
                x.Id, x.CustomerId, x.Customer.Name, x.BookingDate, x.StartTime, x.EndTime, x.Status, x.Notes,
                Treatments = x.Treatments.OrderBy(t => t.Treatment.DisplayOrder).Select(t => new BotTreatmentRef(t.TreatmentId, t.Treatment.Name)).ToList(),
            })
            .SingleAsync(ct);
        return new BotBookingDto(b.Id, b.CustomerId, b.Name, Format(b.BookingDate), Format(b.StartTime), Format(b.EndTime),
            b.Treatments, b.Status.ToString(), b.Notes);
    }

    private static string Format(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string Format(TimeOnly t) => t.ToString("HH:mm", CultureInfo.InvariantCulture);
}
