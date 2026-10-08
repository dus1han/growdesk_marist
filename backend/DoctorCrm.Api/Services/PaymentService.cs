using ClosedXML.Excel;
using DoctorCrm.Api.Data;
using DoctorCrm.Api.DTOs;
using DoctorCrm.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace DoctorCrm.Api.Services;

/// <summary>
/// Consultation payments (spec §28). Every payment is a new entry, never an edit: money received
/// (Paid, the whole balance or part of it) or a balance written off (Waived). What a consultation
/// still owes is its Balance (<see cref="BookingMoney"/>); a customer's outstanding is the sum.
/// </summary>
public class PaymentService(AppDbContext db, AuditService audit, ClinicClock clock, SettingsService settings)
{
    public const int MaxExportRows = 20_000;

    public async Task<PagedResult<PaymentListItemDto>> ListAsync(PaymentQuery q, CancellationToken ct)
    {
        var page = Math.Max(1, q.Page);
        var size = Math.Clamp(q.PageSize, 1, 200);
        var query = await FilterAsync(q, includeStatus: true, includeDates: true, ct);

        var total = await query.CountAsync(ct);
        var items = await Project(query.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id).Skip((page - 1) * size).Take(size))
            .ToListAsync(ct);
        return new PagedResult<PaymentListItemDto>(items, page, size, total);
    }

    public async Task<PaymentSummaryDto> SummaryAsync(PaymentQuery q, CancellationToken ct)
    {
        // Collected and waived: entries recorded in the range.
        var inRange = await FilterAsync(q, includeStatus: false, includeDates: true, ct);
        var collected = await inRange.Where(p => p.Status == PaymentStatus.Paid)
            .GroupBy(_ => 1).Select(g => new { Sum = g.Sum(p => p.Amount), Count = g.Count() }).FirstOrDefaultAsync(ct);
        var waived = await inRange.Where(p => p.Status == PaymentStatus.Waived)
            .GroupBy(_ => 1).Select(g => new { Sum = g.Sum(p => p.Amount), Count = g.Count() }).FirstOrDefaultAsync(ct);

        // Outstanding: every balance still owed today, whatever the range.
        var owed = FilterOutstanding(new OutstandingQuery { CustomerId = q.CustomerId, Search = q.Search });
        var outstanding = await owed.GroupBy(_ => 1).Select(g => new { Sum = g.Sum(b => b.Balance), Count = g.Count() }).FirstOrDefaultAsync(ct);

        var currency = (await settings.GetAsync(ct)).Currency;
        return new PaymentSummaryDto(
            collected?.Sum ?? 0, collected?.Count ?? 0,
            outstanding?.Sum ?? 0, outstanding?.Count ?? 0,
            waived?.Sum ?? 0, waived?.Count ?? 0,
            currency);
    }

    /// <summary>Consultations still owed money, oldest first.</summary>
    public async Task<PagedResult<OutstandingItemDto>> OutstandingAsync(OutstandingQuery q, CancellationToken ct)
    {
        var page = Math.Max(1, q.Page);
        var size = Math.Clamp(q.PageSize, 1, 200);
        var query = FilterOutstanding(q);
        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(b => b.BookingDate).ThenBy(b => b.StartTime).ThenBy(b => b.Id)
            .Skip((page - 1) * size).Take(size)
            .Select(b => new OutstandingItemDto(
                b.Id,
                new NamedRef(b.Customer.Id, b.Customer.Name),
                b.BookingDate, b.StartTime,
                b.Treatments.OrderBy(t => t.Treatment.DisplayOrder).Select(t => t.Treatment.Name).ToList(),
                b.ConsultationCharge ?? 0, b.AmountPaid, b.Balance))
            .ToListAsync(ct);
        return new PagedResult<OutstandingItemDto>(items, page, size, total);
    }

    /// <summary>Money received for a completed consultation: any amount up to its balance.</summary>
    public async Task<PaymentListItemDto> RecordAsync(int bookingId, RecordPaymentRequest r, int? userId, CancellationToken ct)
    {
        var booking = await LoadOwingAsync(bookingId, ct);
        if (r.Amount <= 0)
            throw new BusinessRuleException("Enter the amount received.", field: "amount");
        if (r.Amount > booking.Balance)
            throw new BusinessRuleException($"That's more than the balance of {booking.Balance:#,##0.##}.", field: "amount");
        if (!await db.PaymentMethods.AnyAsync(m => m.Id == r.PaymentMethodId && m.IsActive, ct))
            throw new BusinessRuleException("Choose how the customer paid.", field: "paymentMethodId");

        var today = await clock.TodayAsync(ct);
        if (r.PaymentDate is { } d && d > today)
            throw new BusinessRuleException("The payment date can't be in the future.", field: "paymentDate");

        var now = DateTime.UtcNow;
        var paidAt = r.PaymentDate is { } date && date != today
            ? TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(new TimeOnly(12, 0)), await clock.ZoneAsync(ct))
            : now;

        var payment = new Payment
        {
            BookingId = booking.Id,
            CustomerId = booking.CustomerId,
            Amount = r.Amount,
            Status = PaymentStatus.Paid,
            PaymentMethodId = r.PaymentMethodId,
            PaymentDate = paidAt,
            CreatedById = userId,
            CreatedAt = now,
        };
        booking.Payments.Add(payment);
        BookingMoney.Recalculate(booking);
        // "later": paid after the consultation, so the dashboard's activity shows it (see DashboardService).
        audit.Record(userId, "Payment Recorded", nameof(Booking), booking.Id, new { amount = r.Amount, balance = booking.Balance, later = true });
        await db.SaveChangesAsync(ct);

        return await Project(db.Payments.AsNoTracking().Where(p => p.Id == payment.Id)).SingleAsync(ct);
    }

    /// <summary>Writes off what a consultation still owes.</summary>
    public async Task<PaymentListItemDto> WaiveAsync(int bookingId, int? userId, CancellationToken ct)
    {
        var booking = await LoadOwingAsync(bookingId, ct);
        var waived = booking.Balance;
        var entry = new Payment
        {
            BookingId = booking.Id,
            CustomerId = booking.CustomerId,
            Amount = waived,
            Status = PaymentStatus.Waived,
            CreatedById = userId,
            CreatedAt = DateTime.UtcNow,
        };
        booking.Payments.Add(entry);
        BookingMoney.Recalculate(booking);
        audit.Record(userId, "Balance Waived", nameof(Booking), booking.Id, new { amount = waived });
        await db.SaveChangesAsync(ct);

        return await Project(db.Payments.AsNoTracking().Where(p => p.Id == entry.Id)).SingleAsync(ct);
    }

    private async Task<Booking> LoadOwingAsync(int bookingId, CancellationToken ct)
    {
        var booking = await db.Bookings.Include(b => b.Payments).SingleOrDefaultAsync(b => b.Id == bookingId, ct)
            ?? throw BusinessRuleException.NotFound("Booking");
        if (booking.Status != BookingStatus.Completed)
            throw new BusinessRuleException("Payments are recorded once the consultation is completed.", StatusCodes.Status409Conflict);
        if (booking.Balance <= 0)
            throw new BusinessRuleException("This consultation has nothing left to pay.", StatusCodes.Status409Conflict);
        return booking;
    }

    private IQueryable<Booking> FilterOutstanding(OutstandingQuery q)
    {
        var query = db.Bookings.AsNoTracking().Where(b => b.Balance > 0);
        if (q.CustomerId is { } customerId) query = query.Where(b => b.CustomerId == customerId);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var (like, handle, digits, digitsLike) = SearchTerms(q.Search);
            query = query.Where(b => EF.Functions.ILike(b.Customer.Name, like)
                                     || (b.Customer.InstagramName != null && EF.Functions.Like(b.Customer.InstagramName, handle))
                                     || (digits.Length >= 3 && b.Customer.WhatsAppNumber != null && EF.Functions.Like(b.Customer.WhatsAppNumber, digitsLike)));
        }
        return query;
    }

    private static (string Like, string Handle, string Digits, string DigitsLike) SearchTerms(string search)
    {
        var term = search.Trim();
        var digits = new string(term.Where(char.IsDigit).ToArray()).TrimStart('0');
        return ($"%{term}%", $"%{term.TrimStart('@').ToLowerInvariant()}%", digits, $"%{digits}%");
    }

    public async Task<(byte[] Bytes, string FileName)> ExportAsync(PaymentQuery q, int? userId, CancellationToken ct)
    {
        var query = await FilterAsync(q, includeStatus: true, includeDates: true, ct);
        var count = await query.CountAsync(ct);
        if (count > MaxExportRows)
            throw new BusinessRuleException($"That's {count:N0} payments, more than {MaxExportRows:N0} can be exported at once. Narrow the date range or filters.");

        var rows = await Project(query.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id)).ToListAsync(ct);
        var locale = await settings.GetAsync(ct);
        var zone = await clock.ZoneAsync(ct);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Payments");
        string[] headers = ["Recorded", "Customer", "Consultation date", "Treatments", $"Amount ({locale.Currency})", "Status", "Method", "Paid on", "Recorded by"];
        for (var c = 0; c < headers.Length; c++) sheet.Cell(1, c + 1).Value = headers[c];

        var r = 2;
        foreach (var p in rows)
        {
            sheet.Cell(r, 1).Value = TimeZoneInfo.ConvertTimeFromUtc(p.CreatedAt, zone);
            sheet.Cell(r, 2).Value = p.Customer.Name;
            sheet.Cell(r, 3).Value = p.Booking.Date.ToDateTime(p.Booking.StartTime);
            sheet.Cell(r, 4).Value = string.Join(", ", p.Booking.Treatments);
            sheet.Cell(r, 5).Value = p.Amount;
            sheet.Cell(r, 6).Value = p.Status;
            sheet.Cell(r, 7).Value = p.Method?.Name ?? "";
            if (p.PaymentDate is { } paid) sheet.Cell(r, 8).Value = TimeZoneInfo.ConvertTimeFromUtc(paid, zone);
            sheet.Cell(r, 9).Value = p.RecordedBy ?? "";
            r++;
        }

        var last = Math.Max(1, r - 1);
        var header = sheet.Range(1, 1, 1, headers.Length);
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#4338CA");
        sheet.SheetView.FreezeRows(1);
        if (last > 1)
        {
            sheet.Range(2, 1, last, 1).Style.NumberFormat.Format = "dd mmm yyyy hh:mm";
            sheet.Range(2, 3, last, 3).Style.NumberFormat.Format = "dd mmm yyyy hh:mm";
            sheet.Range(2, 5, last, 5).Style.NumberFormat.Format = "#,##0.00";
            sheet.Range(2, 8, last, 8).Style.NumberFormat.Format = "dd mmm yyyy hh:mm";
            // Total under the amounts.
            sheet.Cell(r, 4).Value = "Total";
            sheet.Cell(r, 4).Style.Font.Bold = true;
            sheet.Cell(r, 5).FormulaA1 = $"SUM(E2:E{last})";
            sheet.Cell(r, 5).Style.Font.Bold = true;
            sheet.Cell(r, 5).Style.NumberFormat.Format = "#,##0.00";
        }
        sheet.Range(1, 1, last, headers.Length).SetAutoFilter();
        sheet.Columns().AdjustToContents(1, Math.Min(last, 500));

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        audit.Record(userId, "Payments Exported", nameof(Payment), null, new { rows = rows.Count, q.From, q.To, q.Status });
        await db.SaveChangesAsync(ct);
        return (stream.ToArray(), $"payments-{DateTime.UtcNow:yyyy-MM-dd-HHmm}.xlsx");
    }

    private async Task<IQueryable<Payment>> FilterAsync(PaymentQuery q, bool includeStatus, bool includeDates, CancellationToken ct)
    {
        // Old "nothing received yet" entries carry no money: what is owed is the booking's balance.
        var query = db.Payments.AsNoTracking().Where(p => p.Status != PaymentStatus.Pending);
        if (includeStatus && Enum.TryParse<PaymentStatus>(q.Status, true, out var status)) query = query.Where(p => p.Status == status);
        if (q.PaymentMethodId is { } methodId) query = query.Where(p => p.PaymentMethodId == methodId);
        if (q.CustomerId is { } customerId) query = query.Where(p => p.CustomerId == customerId);
        if (includeDates && q.From is { } from)
        {
            var start = await clock.StartOfDayUtcAsync(from, ct);
            query = query.Where(p => p.CreatedAt >= start);
        }
        if (includeDates && q.To is { } to)
        {
            var end = await clock.StartOfDayUtcAsync(to.AddDays(1), ct);
            query = query.Where(p => p.CreatedAt < end);
        }
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var (like, handle, digits, digitsLike) = SearchTerms(q.Search);
            query = query.Where(p => EF.Functions.ILike(p.Customer.Name, like)
                                     || (p.Customer.InstagramName != null && EF.Functions.Like(p.Customer.InstagramName, handle))
                                     || (digits.Length >= 3 && p.Customer.WhatsAppNumber != null && EF.Functions.Like(p.Customer.WhatsAppNumber, digitsLike)));
        }
        return query;
    }

    private static IQueryable<PaymentListItemDto> Project(IQueryable<Payment> query) =>
        query.Select(p => new PaymentListItemDto(
            p.Id,
            new NamedRef(p.Customer.Id, p.Customer.Name),
            new PaymentBookingRef(p.Booking.Id, p.Booking.BookingDate, p.Booking.StartTime,
                p.Booking.Treatments.OrderBy(t => t.Treatment.DisplayOrder).Select(t => t.Treatment.Name).ToList()),
            p.Amount,
            p.Status.ToString(),
            p.PaymentMethod != null ? new NamedRef(p.PaymentMethod.Id, p.PaymentMethod.Name) : null,
            p.PaymentDate,
            p.CreatedAt,
            p.CreatedBy != null ? p.CreatedBy.FullName : null,
            p.Booking.Balance));
}
