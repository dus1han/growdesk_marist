using ClosedXML.Excel;
using DoctorCrm.Api.Data;
using DoctorCrm.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace DoctorCrm.Api.Services;

/// <summary>
/// Excel export of the customer list, with the same search and filters as the screen
/// (<see cref="CustomerService.FilterAsync"/>). Money owed is included only for users who may see payments.
/// </summary>
public class CustomerExportService(AppDbContext db, CustomerService customers, SettingsService settings, AuditService audit)
{
    /// <summary>Beyond this the user is asked to narrow the filters rather than wait for a huge file.</summary>
    public const int MaxRows = 20_000;

    private static readonly Dictionary<string, string> ConsultationLabels = new()
    {
        [ConsultationStates.Booked] = "Booked",
        [ConsultationStates.Rescheduled] = "Rescheduled",
        [ConsultationStates.Consulted] = "Consulted",
        [ConsultationStates.Missed] = "Missed",
        [ConsultationStates.Cancelled] = "Cancelled",
    };

    public async Task<(byte[] Bytes, string FileName)> CreateAsync(CustomerQuery q, bool includeMoney, int? userId, CancellationToken ct)
    {
        var query = await customers.FilterAsync(q, ct);
        var count = await query.CountAsync(ct);
        if (count > MaxRows)
            throw new BusinessRuleException($"That's {count:N0} customers, more than {MaxRows:N0} can be exported at once. Narrow the search or filters.");

        var rows = await query
            .OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.WhatsAppNumber,
                c.SecondaryPhone,
                c.InstagramName,
                c.Email,
                Stage = c.Stage.Name,
                LeadSource = c.LeadSource != null ? c.LeadSource.Name : null,
                Treatments = c.Treatments.OrderBy(t => t.Treatment.DisplayOrder).Select(t => t.Treatment.Name).ToList(),
                AssignedUser = c.AssignedUser != null ? c.AssignedUser.FullName : null,
                c.NextFollowUpDate,
                c.LastContactDate,
                Outstanding = db.Bookings.Where(b => b.CustomerId == c.Id).Sum(b => b.Balance),
                c.CreatedAt,
                c.Notes,
                Custom = c.CustomFieldValues.Select(v => new { v.CustomFieldId, v.Value }).ToList(),
            })
            .AsSplitQuery()
            .ToListAsync(ct);

        var consultations = await customers.ConsultationsForAsync(rows.Select(r => r.Id).ToList(), ct);
        var fields = await db.CustomFields.AsNoTracking().Include(f => f.Options).Where(f => f.IsActive).OrderBy(f => f.DisplayOrder).ToListAsync(ct);
        var locale = await settings.GetAsync(ct);
        var zone = TimeZoneInfo.TryFindSystemTimeZoneById(locale.TimeZone, out var tz) ? tz : TimeZoneInfo.Utc;

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Customers");
        var headers = new List<string>
        {
            "Name", "WhatsApp", "Secondary phone", "Instagram", "Email", "Status", "Lead source", "Interested treatments",
            "Assigned to", "Consultation", "Consultation date", "Next follow-up", "Last contact",
        };
        if (includeMoney) headers.Add($"Outstanding ({locale.Currency})");
        headers.AddRange(["Added", "Notes"]);
        headers.AddRange(fields.Select(f => f.Label));
        for (var c = 0; c < headers.Count; c++) sheet.Cell(1, c + 1).Value = headers[c];

        var r = 2;
        foreach (var row in rows)
        {
            var c = 1;
            var consultation = consultations.GetValueOrDefault(row.Id);
            sheet.Cell(r, c++).Value = row.Name;
            sheet.Cell(r, c++).Value = row.WhatsAppNumber is null ? "" : ContactNormalizer.FormatPhone(row.WhatsAppNumber);
            sheet.Cell(r, c++).Value = row.SecondaryPhone is null ? "" : ContactNormalizer.FormatPhone(row.SecondaryPhone);
            sheet.Cell(r, c++).Value = row.InstagramName is null ? "" : "@" + row.InstagramName;
            sheet.Cell(r, c++).Value = row.Email ?? "";
            sheet.Cell(r, c++).Value = row.Stage;
            sheet.Cell(r, c++).Value = row.LeadSource ?? "";
            sheet.Cell(r, c++).Value = string.Join(", ", row.Treatments);
            sheet.Cell(r, c++).Value = row.AssignedUser ?? "";
            sheet.Cell(r, c++).Value = consultation is null ? "" : ConsultationLabels.GetValueOrDefault(consultation.State, "");
            var consultedOn = sheet.Cell(r, c++);
            if (consultation?.Date is { } cd) consultedOn.Value = cd.ToDateTime(consultation.StartTime ?? TimeOnly.MinValue);
            var followUp = sheet.Cell(r, c++);
            if (row.NextFollowUpDate is { } fu) followUp.Value = fu.ToDateTime(TimeOnly.MinValue);
            var lastContact = sheet.Cell(r, c++);
            if (row.LastContactDate is { } lc) lastContact.Value = lc.ToDateTime(TimeOnly.MinValue);
            if (includeMoney) sheet.Cell(r, c++).Value = row.Outstanding;
            sheet.Cell(r, c++).Value = TimeZoneInfo.ConvertTimeFromUtc(row.CreatedAt, zone);
            sheet.Cell(r, c++).Value = row.Notes ?? "";
            foreach (var field in fields)
            {
                var stored = row.Custom.FirstOrDefault(v => v.CustomFieldId == field.Id)?.Value;
                sheet.Cell(r, c++).Value = stored is null ? "" : CustomFieldValueConverter.FromStorage(field, stored).Display;
            }
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
        Format("Consultation date", "dd mmm yyyy hh:mm");
        Format("Next follow-up", "dd mmm yyyy");
        Format("Last contact", "dd mmm yyyy");
        Format("Added", "dd mmm yyyy hh:mm");
        Format($"Outstanding ({locale.Currency})", "#,##0.00");
        sheet.Range(1, 1, last, headers.Count).SetAutoFilter();
        sheet.Columns().AdjustToContents(1, Math.Min(last, 500));

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        audit.Record(userId, "Customers Exported", "Customer", null, new { rows = rows.Count, q.Search, q.StageId, q.LeadSourceId, q.TreatmentId });
        await db.SaveChangesAsync(ct);
        return (stream.ToArray(), $"customers-{DateTime.UtcNow:yyyy-MM-dd-HHmm}.xlsx");
    }
}
