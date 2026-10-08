using ClosedXML.Excel;
using DoctorCrm.Api.Data;
using DoctorCrm.Api.DTOs;
using DoctorCrm.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace DoctorCrm.Api.Services;

/// <summary>
/// Excel export of the bookings list. Uses the same filters as the screen
/// (<see cref="BookingService.Filter"/>), so the file always matches what the user was looking at.
/// </summary>
public class BookingExportService(AppDbContext db, SettingsService settings, AuditService audit)
{
    /// <summary>Beyond this the user is asked to narrow the filters rather than wait for a huge file.</summary>
    public const int MaxRows = 20_000;

    public async Task<(byte[] Bytes, string FileName)> CreateAsync(BookingQuery q, bool includePayments, int? userId, CancellationToken ct)
    {
        var query = BookingService.Filter(db.Bookings.AsNoTracking(), q);
        var count = await query.CountAsync(ct);
        if (count > MaxRows)
            throw new BusinessRuleException($"That's {count:N0} bookings, more than {MaxRows:N0} can be exported at once. Narrow the date range or filters.");

        var rows = await query
            .Select(b => new
            {
                b.BookingDate,
                b.StartTime,
                b.EndTime,
                Customer = b.Customer.Name,
                b.Customer.WhatsAppNumber,
                b.Customer.InstagramName,
                Treatments = b.Treatments.OrderBy(t => t.Treatment.DisplayOrder).Select(t => t.Treatment.Name).ToList(),
                Doctor = b.Doctor != null ? b.Doctor.FullName : null,
                b.Status,
                b.ConsultationCharge,
                b.AmountPaid,
                b.Balance,
                LastPayment = b.Payments.Where(p => p.Status == PaymentStatus.Paid).OrderByDescending(p => p.Id)
                    .Select(p => new { Method = p.PaymentMethod != null ? p.PaymentMethod.Name : null, p.PaymentDate })
                    .FirstOrDefault(),
                NextTreatment = b.NextTreatment != null ? b.NextTreatment.Name : null,
                b.NextTreatmentDate,
                CancellationReason = b.CancellationReason != null ? b.CancellationReason.Name : null,
                b.CancellationNote,
                b.Notes,
            })
            .AsSplitQuery()
            .ToListAsync(ct);

        var locale = await settings.GetAsync(ct);
        var zone = TimeZoneInfo.TryFindSystemTimeZoneById(locale.TimeZone, out var tz) ? tz : TimeZoneInfo.Utc;

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Bookings");

        var headers = new List<string> { "Date", "Start", "End", "Customer", "WhatsApp", "Instagram", "Treatments", "Doctor", "Status" };
        if (includePayments)
            headers.AddRange([$"Charge ({locale.Currency})", $"Paid ({locale.Currency})", $"Balance ({locale.Currency})", "Payment", "Payment method", "Paid on"]);
        headers.AddRange(["Next treatment", "Next treatment date", "Cancellation reason", "Notes"]);

        for (var c = 0; c < headers.Count; c++) sheet.Cell(1, c + 1).Value = headers[c];

        var r = 2;
        foreach (var row in rows)
        {
            var c = 1;
            sheet.Cell(r, c++).Value = row.BookingDate.ToDateTime(TimeOnly.MinValue);
            sheet.Cell(r, c++).Value = row.StartTime.ToTimeSpan();
            sheet.Cell(r, c++).Value = row.EndTime.ToTimeSpan();
            sheet.Cell(r, c++).Value = row.Customer;
            sheet.Cell(r, c++).Value = row.WhatsAppNumber is null ? "" : ContactNormalizer.FormatPhone(row.WhatsAppNumber);
            sheet.Cell(r, c++).Value = row.InstagramName is null ? "" : "@" + row.InstagramName;
            sheet.Cell(r, c++).Value = string.Join(", ", row.Treatments);
            sheet.Cell(r, c++).Value = row.Doctor ?? "";
            sheet.Cell(r, c++).Value = row.Status == BookingStatus.NoShow ? "No-show" : row.Status.ToString();
            if (includePayments)
            {
                var charge = sheet.Cell(r, c++);
                var completed = row.Status == BookingStatus.Completed && row.ConsultationCharge is not null;
                if (row.ConsultationCharge is { } amount) charge.Value = amount;
                var paidCell = sheet.Cell(r, c++);
                var balanceCell = sheet.Cell(r, c++);
                if (completed)
                {
                    paidCell.Value = row.AmountPaid;
                    balanceCell.Value = row.Balance;
                }
                sheet.Cell(r, c++).Value = PaymentLabel(BookingMoney.State(row.Status, row.ConsultationCharge, row.AmountPaid, row.Balance));
                sheet.Cell(r, c++).Value = row.LastPayment?.Method ?? "";
                var paidOn = sheet.Cell(r, c++);
                if (row.LastPayment?.PaymentDate is { } paid) paidOn.Value = TimeZoneInfo.ConvertTimeFromUtc(paid, zone);
            }
            sheet.Cell(r, c++).Value = row.NextTreatment ?? "";
            var next = sheet.Cell(r, c++);
            if (row.NextTreatmentDate is { } nd) next.Value = nd.ToDateTime(TimeOnly.MinValue);
            sheet.Cell(r, c++).Value = row.CancellationReason is null ? "" : row.CancellationNote is null ? row.CancellationReason : $"{row.CancellationReason}: {row.CancellationNote}";
            sheet.Cell(r, c).Value = row.Notes ?? "";
            r++;
        }

        // ---- Formatting ----
        var header = sheet.Range(1, 1, 1, headers.Count);
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#4338CA");
        sheet.SheetView.FreezeRows(1);

        var last = Math.Max(1, r - 1);
        void Format(string name, string format)
        {
            var col = headers.IndexOf(name) + 1;
            if (col > 0 && last > 1) sheet.Range(2, col, last, col).Style.NumberFormat.Format = format;
        }
        Format("Date", "dd mmm yyyy");
        Format("Start", "hh:mm");
        Format("End", "hh:mm");
        Format("Next treatment date", "dd mmm yyyy");
        Format("Paid on", "dd mmm yyyy hh:mm");
        Format($"Charge ({locale.Currency})", "#,##0.00");
        Format($"Paid ({locale.Currency})", "#,##0.00");
        Format($"Balance ({locale.Currency})", "#,##0.00");

        sheet.Range(1, 1, last, headers.Count).SetAutoFilter();
        sheet.Columns().AdjustToContents(1, Math.Min(last, 500));
        foreach (var column in sheet.ColumnsUsed())
            if (column.Width > 50) column.Width = 50;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        audit.Record(userId, "Bookings Exported", nameof(Booking), null, new { rows = rows.Count, q.From, q.To, q.Status, q.Search });
        await db.SaveChangesAsync(ct);

        var fileName = $"bookings-{DateTime.UtcNow:yyyy-MM-dd-HHmm}.xlsx";
        return (stream.ToArray(), fileName);
    }

    private static string PaymentLabel(string? state) => state switch
    {
        BookingMoney.PartlyPaid => "Partly paid",
        BookingMoney.NoCharge => "No charge",
        null => "",
        _ => state,
    };
}
