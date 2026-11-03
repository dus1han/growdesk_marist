using DoctorCrm.Api.Data;
using DoctorCrm.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace DoctorCrm.Api.Services;

/// <summary>
/// The clinic's local time zone (system setting, default Asia/Dubai). Dates the user picks, such
/// as "created today", mean the clinic's day, not the server's UTC day.
/// </summary>
public class ClinicClock(AppDbContext db)
{
    private TimeZoneInfo? _zone;

    public async Task<TimeZoneInfo> ZoneAsync(CancellationToken ct)
    {
        if (_zone is not null) return _zone;
        var id = await db.SystemSettings.AsNoTracking()
            .Where(s => s.Key == SettingKeys.TimeZone).Select(s => s.Value).SingleOrDefaultAsync(ct);
        _zone = id is not null && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var tz) ? tz : TimeZoneInfo.Utc;
        return _zone;
    }

    /// <summary>Today's date in the clinic's time zone.</summary>
    public async Task<DateOnly> TodayAsync(CancellationToken ct) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, await ZoneAsync(ct)));

    /// <summary>UTC instant at which the given clinic-local date starts.</summary>
    public async Task<DateTime> StartOfDayUtcAsync(DateOnly date, CancellationToken ct) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue), await ZoneAsync(ct));

    /// <summary>UTC instant of a clinic-local date and time.</summary>
    public async Task<DateTime> ToUtcAsync(DateTime local, CancellationToken ct) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), await ZoneAsync(ct));
}
