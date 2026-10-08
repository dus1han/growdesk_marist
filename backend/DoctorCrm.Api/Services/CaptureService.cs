using System.Text.Json;
using System.Text.RegularExpressions;
using DoctorCrm.Api.Data;
using DoctorCrm.Api.DTOs;
using DoctorCrm.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace DoctorCrm.Api.Services;

/// <summary>
/// What the external capture tool reads and writes (spec §30–§35). The tool owns no business
/// logic: this service validates against the admin's capture configuration, normalises contact
/// details, finds the existing customer and decides what may change.
/// </summary>
public partial class CaptureService(AppDbContext db, AuditService audit, ContactNormalizer contacts, ClinicClock clock)
{
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    // ---- Configuration ------------------------------------------------------------------------

    public async Task<CaptureConfigDto> ConfigAsync(CancellationToken ct)
    {
        var rows = await db.CaptureFieldConfigurations.AsNoTracking()
            .Include(c => c.CustomField).ThenInclude(f => f!.Options)
            .OrderBy(c => c.DisplayOrder)
            .ToListAsync(ct);

        var fields = new List<CaptureConfigFieldDto>();
        foreach (var row in rows)
        {
            if (row.CustomField is { } cf)
            {
                if (!cf.IsActive) continue;
                fields.Add(new CaptureConfigFieldDto(row.FieldKey, cf.Label, cf.FieldType.ToString().ToLowerInvariant(),
                    row.IsEnabled, row.IsRequired, row.DisplayOrder, IsCustom: true, cf.HasOptions ? Options(cf) : null));
            }
            else if (CaptureFields.Find(row.FieldKey) is { } builtIn)
            {
                fields.Add(new CaptureConfigFieldDto(row.FieldKey, builtIn.Label, builtIn.Type,
                    row.IsEnabled, row.IsRequired, row.DisplayOrder, IsCustom: false, null));
            }
        }
        return new CaptureConfigDto(fields);
    }

    public async Task<IReadOnlyList<CaptureLookupDto>> TreatmentsAsync(CancellationToken ct) =>
        await db.Treatments.AsNoTracking().Where(t => t.IsActive).OrderBy(t => t.DisplayOrder).ThenBy(t => t.Name)
            .Select(t => new CaptureLookupDto(t.Id, t.Name, null)).ToListAsync(ct);

    public async Task<IReadOnlyList<CaptureLookupDto>> StagesAsync(CancellationToken ct) =>
        await db.Stages.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.DisplayOrder).ThenBy(s => s.Name)
            .Select(s => new CaptureLookupDto(s.Id, s.Name, s.Color)).ToListAsync(ct);

    public async Task<IReadOnlyList<CaptureLookupDto>> SourcesAsync(CancellationToken ct) =>
        await db.LeadSources.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.DisplayOrder).ThenBy(s => s.Name)
            .Select(s => new CaptureLookupDto(s.Id, s.Name, null)).ToListAsync(ct);

    public async Task<IReadOnlyList<CaptureCustomFieldDto>> CustomFieldsAsync(CancellationToken ct)
    {
        var fields = await db.CustomFields.AsNoTracking().Include(f => f.Options)
            .Where(f => f.IsActive).OrderBy(f => f.DisplayOrder).ToListAsync(ct);
        return fields.Select(f => new CaptureCustomFieldDto(f.Key, f.Label, f.FieldType.ToString().ToLowerInvariant(),
            f.HasOptions ? Options(f) : [])).ToList();
    }

    private static IReadOnlyList<CaptureOptionDto> Options(CustomField f) =>
        f.Options.Where(o => o.IsActive).OrderBy(o => o.DisplayOrder).Select(o => new CaptureOptionDto(o.Id, o.Label)).ToList();

    // ---- Capturing a customer -----------------------------------------------------------------

    /// <summary>
    /// Finds the customer by WhatsApp number, then Instagram name, and updates them; otherwise
    /// creates one (spec §34, §35). An update only fills in what was captured: treatments are
    /// added, notes appended, and nothing already recorded is cleared.
    /// </summary>
    public async Task<CaptureCustomerResultDto> CaptureAsync(CaptureCustomerRequest r, string clientName, CancellationToken ct)
    {
        var lead = await ValidateAsync(r, ct);
        var warnings = new List<string>();
        var today = await clock.TodayAsync(ct);

        var customer = await FindAsync(lead.WhatsApp, lead.Instagram, ct);
        if (customer is null)
        {
            customer = new Customer
            {
                Name = lead.Name ?? (lead.Instagram is not null ? "@" + lead.Instagram : ContactNormalizer.FormatPhone(lead.WhatsApp!)),
                WhatsAppNumber = lead.WhatsApp,
                InstagramName = lead.Instagram,
                SecondaryPhone = lead.SecondaryPhone,
                Email = lead.Email,
                StageId = lead.StageId ?? await db.Stages.Where(s => s.SystemKey == StageKeys.Interested).Select(s => s.Id).SingleAsync(ct),
                LeadSourceId = lead.LeadSourceId,
                Notes = lead.Notes,
                LastContactDate = today,
            };
            foreach (var id in lead.TreatmentIds)
                customer.Treatments.Add(new CustomerTreatment { TreatmentId = id, CreatedAt = DateTime.UtcNow });
            foreach (var (fieldId, value) in lead.CustomValues)
                customer.CustomFieldValues.Add(new CustomerCustomFieldValue { CustomFieldId = fieldId, Value = value, UpdatedAt = DateTime.UtcNow });

            db.Customers.Add(customer);
            await SaveAsync(ct);
            audit.Record(null, "Customer Created", nameof(Customer), customer.Id, new { customer.Name, source = "capture", client = clientName });
            await db.SaveChangesAsync(ct);
            return new CaptureCustomerResultDto(customer.Id, "created", customer.Name, warnings);
        }

        await UpdateAsync(customer, lead, today, warnings, clientName, ct);
        return new CaptureCustomerResultDto(customer.Id, "updated", customer.Name, warnings);
    }

    private async Task UpdateAsync(Customer customer, ValidLead lead, DateOnly today, List<string> warnings, string clientName, CancellationToken ct)
    {
        var changed = new List<string>();
        void Set<T>(string field, T current, T value, Action<T> apply)
        {
            if (value is null || EqualityComparer<T>.Default.Equals(current, value)) return;
            apply(value);
            changed.Add(field);
        }

        Set(nameof(Customer.Name), customer.Name, lead.Name, v => customer.Name = v!);
        Set(nameof(Customer.Email), customer.Email, lead.Email, v => customer.Email = v);
        // Lead source records where the customer first came from: kept once known, filled in if missing.
        if (customer.LeadSourceId is null)
            Set(nameof(Customer.LeadSourceId), customer.LeadSourceId, lead.LeadSourceId, v => customer.LeadSourceId = v);
        Set(nameof(Customer.SecondaryPhone), customer.SecondaryPhone, lead.SecondaryPhone, v => customer.SecondaryPhone = v);

        // Matched by WhatsApp: a new Instagram name is added unless it belongs to someone else.
        if (lead.Instagram is not null && lead.Instagram != customer.InstagramName)
        {
            var owner = await db.Customers.AsNoTracking().Where(c => c.InstagramName == lead.Instagram && c.Id != customer.Id)
                .Select(c => c.Name).FirstOrDefaultAsync(ct);
            if (owner is null) Set(nameof(Customer.InstagramName), customer.InstagramName, lead.Instagram, v => customer.InstagramName = v);
            else warnings.Add($"Instagram {lead.Instagram} belongs to {owner}, so it was not added.");
        }

        // Matched by Instagram: a different WhatsApp number never replaces the one on record.
        if (lead.WhatsApp is not null && lead.WhatsApp != customer.WhatsAppNumber)
        {
            var shown = ContactNormalizer.FormatPhone(lead.WhatsApp);
            if (customer.WhatsAppNumber is null) Set(nameof(Customer.WhatsAppNumber), customer.WhatsAppNumber, lead.WhatsApp, v => customer.WhatsAppNumber = v);
            else if (customer.SecondaryPhone is null || customer.SecondaryPhone == lead.WhatsApp)
            {
                Set(nameof(Customer.SecondaryPhone), customer.SecondaryPhone, lead.WhatsApp, v => customer.SecondaryPhone = v);
                warnings.Add($"{shown} was saved as the secondary number; the WhatsApp number on record was kept.");
            }
            else warnings.Add($"{shown} was not saved: this customer already has a WhatsApp and a secondary number.");
        }

        if (lead.Notes is not null && customer.Notes?.Contains(lead.Notes, StringComparison.Ordinal) != true)
        {
            customer.Notes = string.IsNullOrEmpty(customer.Notes) ? lead.Notes : $"{customer.Notes}\n\n{lead.Notes}";
            changed.Add(nameof(Customer.Notes));
        }

        // Status is the clinic's own judgement: what the person capturing picked is applied as chosen.
        if (lead.StageId is { } stageId && stageId != customer.StageId)
        {
            var current = await db.Stages.AsNoTracking().SingleAsync(s => s.Id == customer.StageId, ct);
            var target = await db.Stages.AsNoTracking().SingleAsync(s => s.Id == stageId, ct);
            customer.StageId = stageId;
            audit.Record(null, "Stage Changed", nameof(Customer), customer.Id, new { from = current.Name, to = target.Name, source = "capture", client = clientName });
        }

        var known = customer.Treatments.Select(t => t.TreatmentId).ToHashSet();
        var added = lead.TreatmentIds.Where(id => !known.Contains(id)).ToList();
        foreach (var id in added)
            customer.Treatments.Add(new CustomerTreatment { TreatmentId = id, CreatedAt = DateTime.UtcNow });
        if (added.Count > 0)
        {
            var names = await db.Treatments.Where(t => added.Contains(t.Id)).Select(t => t.Name).ToListAsync(ct);
            audit.Record(null, "Treatment Added", nameof(Customer), customer.Id, new { treatments = names, source = "capture", client = clientName });
        }

        foreach (var (fieldId, value) in lead.CustomValues)
        {
            var existing = customer.CustomFieldValues.FirstOrDefault(v => v.CustomFieldId == fieldId);
            if (existing is null)
                customer.CustomFieldValues.Add(new CustomerCustomFieldValue { CustomFieldId = fieldId, Value = value, UpdatedAt = DateTime.UtcNow });
            else if (existing.Value != value)
            {
                existing.Value = value;
                existing.UpdatedAt = DateTime.UtcNow;
            }
            else continue;
            if (!changed.Contains("CustomFields")) changed.Add("CustomFields");
        }

        customer.LastContactDate = today;
        if (changed.Count > 0)
            audit.Record(null, "Customer Updated", nameof(Customer), customer.Id, new { fields = changed, source = "capture", client = clientName });
        await SaveAsync(ct);
    }

    private async Task<Customer?> FindAsync(string? whatsApp, string? instagram, CancellationToken ct)
    {
        IQueryable<Customer> withDetails = db.Customers
            .Include(c => c.Treatments)
            .Include(c => c.CustomFieldValues);
        if (whatsApp is not null && await withDetails.FirstOrDefaultAsync(c => c.WhatsAppNumber == whatsApp, ct) is { } byNumber)
            return byNumber;
        if (instagram is not null && await withDetails.FirstOrDefaultAsync(c => c.InstagramName == instagram, ct) is { } byHandle)
            return byHandle;
        return null;
    }

    /// <summary>Two devices capturing the same new number at once: the second one gets a clear retry message.</summary>
    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            throw BusinessRuleException.Conflict("This customer was just saved from another device. Please send it again.");
        }
    }

    // ---- Validation ---------------------------------------------------------------------------

    private async Task<int> SiteSourceIdAsync(string site, CancellationToken ct)
    {
        var key = site.Trim().ToLowerInvariant();
        if (key is not (LeadSourceKeys.WhatsApp or LeadSourceKeys.Instagram))
            throw new BusinessRuleException("The capture tool sent an unknown site. Update GrowDesk Capture.", field: "source");
        return await db.LeadSources.Where(s => s.SystemKey == key).Select(s => (int?)s.Id).SingleOrDefaultAsync(ct)
            ?? throw new InvalidOperationException($"The built-in lead source '{key}' is missing.");
    }

    private sealed record ValidLead(
        string? Name,
        string? WhatsApp,
        string? SecondaryPhone,
        string? Instagram,
        string? Email,
        int? StageId,
        int? LeadSourceId,
        IReadOnlyList<int> TreatmentIds,
        IReadOnlyList<(int FieldId, string Value)> CustomValues,
        string? Notes);

    /// <summary>
    /// Keeps only the fields the admin enabled, checks the required ones are present and that
    /// every value is valid. Values for switched-off fields are ignored, not rejected.
    /// </summary>
    private async Task<ValidLead> ValidateAsync(CaptureCustomerRequest r, CancellationToken ct)
    {
        var config = await db.CaptureFieldConfigurations.AsNoTracking()
            .Include(c => c.CustomField).ThenInclude(f => f!.Options)
            .Where(c => c.IsEnabled && (c.CustomField == null || c.CustomField.IsActive))
            .OrderBy(c => c.DisplayOrder)
            .ToListAsync(ct);
        bool On(string key) => config.Any(c => c.FieldKey == key && c.CustomField == null);

        static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

        var name = On(CaptureFields.Name) ? Clean(r.Name) : null;
        var rawWhatsApp = On(CaptureFields.WhatsApp) ? Clean(r.WhatsApp) : null;
        var rawSecondary = On("secondary_phone") ? Clean(r.SecondaryPhone) : null;
        var rawInstagram = On("instagram") ? Clean(r.Instagram) : null;
        var email = On("email") ? Clean(r.Email)?.ToLowerInvariant() : null;
        var notes = On("notes") ? Clean(r.Notes) : null;
        var stageId = On("stage") ? r.StageId : null;
        // The toolbar says which site it captured on, and that is the lead source. Older toolbars
        // don't, and may send a chosen source instead (when the admin shows that field).
        var sourceId = r.Source is { } site ? await SiteSourceIdAsync(site, ct) : On("lead_source") ? r.LeadSourceId : null;
        var treatmentIds = On("treatments") ? (r.TreatmentIds ?? []).Distinct().ToList() : [];
        var customInput = r.CustomFields ?? [];

        // Required fields, as the admin configured them.
        var missing = config.Where(c => c.IsRequired && c.FieldKey switch
        {
            CaptureFields.Name => name is null,
            CaptureFields.WhatsApp => rawWhatsApp is null,
            "secondary_phone" => rawSecondary is null,
            "instagram" => rawInstagram is null,
            "email" => email is null,
            "notes" => notes is null,
            "stage" => stageId is null,
            "lead_source" => sourceId is null,
            "treatments" => treatmentIds.Count == 0,
            var key => c.CustomField is not null && (!customInput.TryGetValue(key, out var v)
                        || CustomFieldValueConverter.ToStorage(c.CustomField, v) is null),
        }).ToList();
        if (missing.Count > 0)
        {
            var labels = missing.Select(c => c.CustomField?.Label ?? CaptureFields.Find(c.FieldKey)?.Label ?? c.FieldKey);
            var first = missing[0];
            throw new BusinessRuleException($"Required: {string.Join(", ", labels)}.",
                field: first.CustomField is null ? first.FieldKey : $"customFields.{first.FieldKey}");
        }

        // Contact details, normalised as the CRM stores them.
        var whatsApp = contacts.NormalizePhone(rawWhatsApp);
        if (rawWhatsApp is not null && whatsApp is null)
            throw new BusinessRuleException("Enter a valid WhatsApp number with its country code, e.g. +94 77 123 4567.", field: "whatsapp");
        var instagram = ContactNormalizer.NormalizeInstagram(rawInstagram);
        if (rawInstagram is not null && instagram is null)
            throw new BusinessRuleException("Enter a valid Instagram name, e.g. @sarah.fernando.", field: "instagram");
        if (whatsApp is null && instagram is null)
            throw new BusinessRuleException("Capture a WhatsApp number or an Instagram name, so the customer can be found again.", field: "whatsapp");
        var secondary = contacts.NormalizePhone(rawSecondary);
        if (rawSecondary is not null && secondary is null)
            throw new BusinessRuleException("Enter a valid secondary number with its country code.", field: "secondary_phone");
        if (email is not null && !EmailPattern().IsMatch(email))
            throw new BusinessRuleException("Enter a valid email address.", field: "email");
        if (name is { Length: > 150 }) throw new BusinessRuleException("Names are at most 150 characters.", field: "name");
        if (notes is { Length: > 4000 }) throw new BusinessRuleException("Notes are at most 4,000 characters.", field: "notes");

        // Lists: only active choices.
        if (stageId is { } sid && !await db.Stages.AnyAsync(s => s.Id == sid && s.IsActive, ct))
            throw new BusinessRuleException("That stage is no longer available. Reload the capture tool.", field: "stage");
        if (sourceId is { } lid && !await db.LeadSources.AnyAsync(s => s.Id == lid && s.IsActive, ct))
            throw new BusinessRuleException("That lead source is no longer available. Reload the capture tool.", field: "lead_source");
        if (treatmentIds.Count > 0 && await db.Treatments.CountAsync(t => treatmentIds.Contains(t.Id) && t.IsActive, ct) != treatmentIds.Count)
            throw new BusinessRuleException("One of the selected treatments is no longer available. Reload the capture tool.", field: "treatments");

        // Custom fields: enabled ones only, validated by type.
        var customValues = new List<(int, string)>();
        foreach (var row in config.Where(c => c.CustomField is not null))
        {
            if (!customInput.TryGetValue(row.FieldKey, out JsonElement raw)) continue;
            if (CustomFieldValueConverter.ToStorage(row.CustomField!, raw) is { } stored) customValues.Add((row.CustomField!.Id, stored));
        }

        return new ValidLead(name, whatsApp, secondary, instagram, email, stageId, sourceId, treatmentIds, customValues, notes);
    }
}
