using DoctorCrm.Api.Data;
using DoctorCrm.Api.DTOs;
using DoctorCrm.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace DoctorCrm.Api.Services;

/// <summary>
/// Follow-up tracking. Every new enquiry is due a follow-up the same day. Each follow-up logged
/// schedules the next one (every <see cref="FollowUpSettingsDto.IntervalDays"/> days) until the
/// customer books, says they're not interested, or, after <see cref="FollowUpSettingsDto.MaxAttempts"/>
/// attempts, staff are prompted to mark them Lost. A booking stops the follow-ups; a cancellation
/// or no-show starts them again the next day.
/// </summary>
public class FollowUpService(AppDbContext db, ClinicClock clock, AuditService audit, StageAutomation stages)
{
    public const int DefaultIntervalDays = 2;
    public const int DefaultMaxAttempts = 4;
    public const string DefaultReminderTimes = "10:00,14:00,17:00";

    /// <summary>A customer added this recently and not yet contacted is a "new enquiry".</summary>
    private const int NewForDays = 2;

    public static readonly string[] Outcomes = [FollowUpOutcomes.CallBack, FollowUpOutcomes.NoAnswer, FollowUpOutcomes.NotInterested];

    // ---- Rules other services apply ----------------------------------------------------------

    /// <summary>Starts the follow-up cycle afresh: first contact due on <paramref name="due"/>.</summary>
    public static void Start(Customer customer, DateOnly due)
    {
        customer.NextFollowUpDate = due;
        customer.FollowUpAttempts = 0;
    }

    /// <summary>Booked: nothing to chase.</summary>
    public static void Stop(Customer customer)
    {
        customer.NextFollowUpDate = null;
        customer.FollowUpAttempts = 0;
    }

    // ---- The queue ---------------------------------------------------------------------------

    /// <summary>
    /// Follow-ups due today or overdue. "mine": the user's own customers plus unassigned ones;
    /// "all": everyone's.
    /// </summary>
    public async Task<FollowUpQueueDto> QueueAsync(string? scope, int userId, CancellationToken ct)
    {
        var today = await clock.TodayAsync(ct);
        var newSince = await NewSinceAsync(today, ct);
        var settings = await GetSettingsAsync(ct);

        var rows = await Due(Scope(scope, userId), today)
            .OrderBy(c => c.NextFollowUpDate).ThenByDescending(c => c.CreatedAt)
            .Take(500)
            .Select(c => new
            {
                c.Id, c.Name, c.WhatsAppNumber, c.InstagramName,
                Stage = new StageRef(c.Stage.Id, c.Stage.Name, c.Stage.Color, c.Stage.SystemKey),
                Treatments = c.Treatments.OrderBy(t => t.Treatment.DisplayOrder).Select(t => t.Treatment.Name).ToList(),
                LeadSource = c.LeadSource != null ? c.LeadSource.Name : null,
                AssignedUser = c.AssignedUser != null ? new NamedRef(c.AssignedUser.Id, c.AssignedUser.FullName) : null,
                Due = c.NextFollowUpDate!.Value, c.FollowUpAttempts, c.LastContactDate, c.LastFollowUpAt, c.CreatedAt,
            })
            .AsSplitQuery()
            .ToListAsync(ct);

        var items = rows.Select(r => new FollowUpItemDto(
            r.Id, r.Name, r.WhatsAppNumber is null ? null : ContactNormalizer.FormatPhone(r.WhatsAppNumber), r.InstagramName,
            r.Stage, r.Treatments, r.LeadSource, r.AssignedUser, r.Due, r.FollowUpAttempts, r.LastContactDate, r.CreatedAt,
            IsNew(r.FollowUpAttempts, r.LastFollowUpAt, r.CreatedAt, newSince))).ToList();
        return new FollowUpQueueDto(today, settings.MaxAttempts, items, await NextTreatmentsAsync(scope, userId, today, ct));
    }

    /// <summary>
    /// Next treatments set when completing a consultation but never booked: from a month ago on,
    /// while the customer has nothing else booked.
    /// </summary>
    private async Task<IReadOnlyList<NextTreatmentItemDto>> NextTreatmentsAsync(string? scope, int userId, DateOnly today, CancellationToken ct)
    {
        var all = string.Equals(scope, "all", StringComparison.OrdinalIgnoreCase);
        var since = today.AddDays(-30);
        var rows = await db.Bookings.AsNoTracking()
            .Where(b => b.Status == BookingStatus.Completed && b.NextTreatmentDate != null && b.NextTreatmentDate >= since
                        && b.NextBookingId == null
                        && b.Customer.IsActive
                        && (all || b.Customer.AssignedUserId == null || b.Customer.AssignedUserId == userId)
                        && !db.Bookings.Any(x => x.CustomerId == b.CustomerId && x.Status == BookingStatus.Booked && x.BookingDate >= today))
            .OrderBy(b => b.NextTreatmentDate).ThenBy(b => b.Customer.Name)
            .Take(200)
            .Select(b => new
            {
                b.Id, b.CustomerId, Customer = b.Customer.Name, b.Customer.WhatsAppNumber, Date = b.NextTreatmentDate!.Value,
                Treatment = b.NextTreatment != null ? new NamedRef(b.NextTreatment.Id, b.NextTreatment.Name) : null,
                Doctor = b.Doctor != null ? new NamedRef(b.Doctor.Id, b.Doctor.FullName) : null,
            })
            .ToListAsync(ct);
        return rows.Select(r => new NextTreatmentItemDto(r.Id, r.CustomerId, r.Customer,
            r.WhatsAppNumber is null ? null : ContactNormalizer.FormatPhone(r.WhatsAppNumber), r.Date, r.Treatment, r.Doctor)).ToList();
    }

    /// <summary>Counts for the sidebar badge and reminders, plus today's new enquiries for alerts.</summary>
    public async Task<FollowUpSummaryDto> SummaryAsync(int userId, CancellationToken ct)
    {
        var today = await clock.TodayAsync(ct);
        var newSince = await NewSinceAsync(today, ct);
        var mine = Scope("mine", userId);
        var due = Due(mine, today);

        var counts = await due.GroupBy(_ => 1).Select(g => new
        {
            Due = g.Count(),
            Overdue = g.Count(c => c.NextFollowUpDate < today),
            New = g.Count(c => c.FollowUpAttempts == 0 && c.LastFollowUpAt == null && c.CreatedAt >= newSince),
        }).FirstOrDefaultAsync(ct);

        // Enquiries that arrived in the last day and nobody has contacted yet: each open screen
        // alerts once per enquiry.
        var dayAgo = DateTime.UtcNow.AddDays(-1);
        var fresh = await due.Where(c => c.FollowUpAttempts == 0 && c.LastFollowUpAt == null && c.CreatedAt >= dayAgo)
            .OrderByDescending(c => c.CreatedAt).Take(20)
            .Select(c => new NewEnquiryDto(c.Id, c.Name, c.CreatedAt, c.LeadSource != null ? c.LeadSource.Name : null,
                c.Treatments.OrderBy(t => t.Treatment.DisplayOrder).Select(t => t.Treatment.Name).FirstOrDefault()))
            .ToListAsync(ct);

        var settings = await GetSettingsAsync(ct);
        return new FollowUpSummaryDto(today, counts?.Due ?? 0, counts?.Overdue ?? 0, counts?.New ?? 0, fresh, settings.ReminderTimes);
    }

    // ---- Logging a follow-up -----------------------------------------------------------------

    public async Task<FollowUpLoggedDto> LogAsync(int customerId, LogFollowUpRequest r, int userId, CancellationToken ct)
    {
        var customer = await db.Customers.SingleOrDefaultAsync(c => c.Id == customerId, ct) ?? throw BusinessRuleException.NotFound("Customer");
        var outcome = Outcomes.FirstOrDefault(o => string.Equals(o, r.Outcome?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new BusinessRuleException("Choose how the follow-up went.", field: "outcome");
        var today = await clock.TodayAsync(ct);
        var settings = await GetSettingsAsync(ct);

        DateOnly? next = null;
        if (outcome == FollowUpOutcomes.NotInterested)
        {
            await stages.MoveAsync(customer, [StageKeys.Interested, StageKeys.FollowUp], StageKeys.Lost, userId, "Not interested (follow-up)", ct);
        }
        else
        {
            next = r.NextFollowUpDate ?? today.AddDays(settings.IntervalDays);
            if (next < today)
                throw new BusinessRuleException("The next follow-up can't be in the past.", field: "nextFollowUpDate");
            if (next > today.AddDays(365))
                throw new BusinessRuleException("Choose a next follow-up within a year.", field: "nextFollowUpDate");
            await stages.MoveAsync(customer, [StageKeys.Interested], StageKeys.FollowUp, userId, "Followed up", ct);
        }

        customer.FollowUpAttempts++;
        customer.LastFollowUpAt = DateTime.UtcNow;
        customer.LastContactDate = today;
        customer.NextFollowUpDate = next;

        var note = string.IsNullOrWhiteSpace(r.Note) ? null : r.Note.Trim();
        audit.Record(userId, "Follow-up Logged", nameof(Customer), customer.Id,
            new { outcome, note, next, attempt = customer.FollowUpAttempts });
        await db.SaveChangesAsync(ct);

        var stage = await db.Stages.AsNoTracking().Where(s => s.Id == customer.StageId).Select(s => s.Name).SingleAsync(ct);
        return new FollowUpLoggedDto(customer.NextFollowUpDate, customer.FollowUpAttempts,
            next is not null && customer.FollowUpAttempts >= settings.MaxAttempts, stage);
    }

    // ---- Settings ----------------------------------------------------------------------------

    public async Task<FollowUpSettingsDto> GetSettingsAsync(CancellationToken ct)
    {
        string[] keys = [SettingKeys.FollowUpIntervalDays, SettingKeys.FollowUpMaxAttempts, SettingKeys.FollowUpReminderTimes];
        var v = await db.SystemSettings.AsNoTracking().Where(s => keys.Contains(s.Key)).ToDictionaryAsync(s => s.Key, s => s.Value, ct);
        return new FollowUpSettingsDto(
            int.TryParse(v.GetValueOrDefault(SettingKeys.FollowUpIntervalDays), out var interval) && interval > 0 ? interval : DefaultIntervalDays,
            int.TryParse(v.GetValueOrDefault(SettingKeys.FollowUpMaxAttempts), out var max) && max > 0 ? max : DefaultMaxAttempts,
            ParseTimes(v.GetValueOrDefault(SettingKeys.FollowUpReminderTimes) ?? DefaultReminderTimes));
    }

    public async Task<FollowUpSettingsDto> SaveSettingsAsync(FollowUpSettingsDto r, int userId, CancellationToken ct)
    {
        var times = ParseTimes(string.Join(",", r.ReminderTimes));
        if (times.Count != r.ReminderTimes.Count(t => !string.IsNullOrWhiteSpace(t)))
            throw new BusinessRuleException("Enter reminder times as HH:mm, e.g. 10:00.", field: "reminderTimes");

        var values = new Dictionary<string, string>
        {
            [SettingKeys.FollowUpIntervalDays] = r.IntervalDays.ToString(),
            [SettingKeys.FollowUpMaxAttempts] = r.MaxAttempts.ToString(),
            [SettingKeys.FollowUpReminderTimes] = string.Join(",", times),
        };
        var existing = await db.SystemSettings.Where(s => values.Keys.Contains(s.Key)).ToDictionaryAsync(s => s.Key, ct);
        foreach (var (key, value) in values)
        {
            if (existing.TryGetValue(key, out var row)) { row.Value = value; row.UpdatedAt = DateTime.UtcNow; }
            else db.SystemSettings.Add(new SystemSetting { Key = key, Value = value, UpdatedAt = DateTime.UtcNow });
        }
        audit.Record(userId, "Follow-up Settings Updated", nameof(SystemSetting), null,
            new { r.IntervalDays, r.MaxAttempts, reminderTimes = times });
        await db.SaveChangesAsync(ct);
        return await GetSettingsAsync(ct);
    }

    // ---- Helpers -----------------------------------------------------------------------------

    private IQueryable<Customer> Scope(string? scope, int userId) =>
        string.Equals(scope, "all", StringComparison.OrdinalIgnoreCase)
            ? db.Customers.AsNoTracking()
            : db.Customers.AsNoTracking().Where(c => c.AssignedUserId == null || c.AssignedUserId == userId);

    private static IQueryable<Customer> Due(IQueryable<Customer> customers, DateOnly today) =>
        customers.Where(c => c.IsActive && c.NextFollowUpDate != null && c.NextFollowUpDate <= today);

    private async Task<DateTime> NewSinceAsync(DateOnly today, CancellationToken ct) =>
        await clock.StartOfDayUtcAsync(today.AddDays(-NewForDays), ct);

    private static bool IsNew(int attempts, DateTime? lastFollowUp, DateTime createdAt, DateTime newSince) =>
        attempts == 0 && lastFollowUp is null && createdAt >= newSince;

    /// <summary>"10:00, 14:00,17:00" → ["10:00", "14:00", "17:00"], sorted, valid times only.</summary>
    private static IReadOnlyList<string> ParseTimes(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => TimeOnly.TryParseExact(t, ["H:mm", "HH:mm"], out var time) ? time : (TimeOnly?)null)
            .OfType<TimeOnly>().Distinct().Order().Select(t => t.ToString("HH:mm")).ToList();
}

public static class FollowUpOutcomes
{
    /// <summary>Spoke to them: interested, call again.</summary>
    public const string CallBack = "CallBack";
    public const string NoAnswer = "NoAnswer";
    public const string NotInterested = "NotInterested";
}
