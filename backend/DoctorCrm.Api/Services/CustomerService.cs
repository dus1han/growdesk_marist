using System.Text.Json;
using DoctorCrm.Api.Data;
using DoctorCrm.Api.DTOs;
using DoctorCrm.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace DoctorCrm.Api.Services;

/// <summary>
/// Customer management (spec §12–§15). WhatsApp number, then Instagram name, identify a customer:
/// both are stored normalised and unique, so the same person can't be added twice.
/// </summary>
public class CustomerService(AppDbContext db, AuditService audit, ContactNormalizer contacts, ClinicClock clock)
{
    public const int MaxPageSize = 100;

    public async Task<PagedResult<CustomerListItemDto>> ListAsync(CustomerQuery q, CancellationToken ct)
    {
        var page = Math.Max(1, q.Page);
        var size = Math.Clamp(q.PageSize, 1, MaxPageSize);
        var query = db.Customers.AsNoTracking().Where(c => c.IsActive);

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var term = q.Search.Trim();
            var like = $"%{term}%";
            var handle = $"%{term.TrimStart('@').ToLowerInvariant()}%";
            // Numbers are stored with the country code, so "050 123" must match "+97150123…".
            var digits = new string(term.Where(char.IsDigit).ToArray()).TrimStart('0');
            var digitsLike = $"%{digits}%";
            query = query.Where(c => EF.Functions.ILike(c.Name, like)
                                     || (c.InstagramName != null && EF.Functions.Like(c.InstagramName, handle))
                                     || (digits.Length >= 3 && c.WhatsAppNumber != null && EF.Functions.Like(c.WhatsAppNumber, digitsLike))
                                     || (digits.Length >= 3 && c.SecondaryPhone != null && EF.Functions.Like(c.SecondaryPhone, digitsLike)));
        }

        if (q.StageId is { } stageId) query = query.Where(c => c.StageId == stageId);
        query = FilterConsultation(query, q.Consultation);
        if (q.TreatmentId is { } treatmentId) query = query.Where(c => c.Treatments.Any(t => t.TreatmentId == treatmentId));
        if (q.LeadSourceId is { } sourceId) query = query.Where(c => c.LeadSourceId == sourceId);
        if (q.AssignedUserId is { } userId) query = query.Where(c => c.AssignedUserId == userId);
        if (q.CreatedFrom is { } cf)
        {
            var from = await clock.StartOfDayUtcAsync(cf, ct);
            query = query.Where(c => c.CreatedAt >= from);
        }
        if (q.CreatedTo is { } cto)
        {
            var to = await clock.StartOfDayUtcAsync(cto.AddDays(1), ct);
            query = query.Where(c => c.CreatedAt < to);
        }
        if (q.FollowUpFrom is { } ff) query = query.Where(c => c.NextFollowUpDate >= ff);
        if (q.FollowUpTo is { } ft) query = query.Where(c => c.NextFollowUpDate <= ft);
        if (q.HasOutstanding is { } owes)
            query = query.Where(c => db.Bookings.Any(b => b.CustomerId == c.Id && b.Balance > 0) == owes);

        var total = await query.CountAsync(ct);
        var today = await clock.TodayAsync(ct);
        var rows = await query
            .OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id)
            .Skip((page - 1) * size).Take(size)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.WhatsAppNumber,
                c.InstagramName,
                Stage = new StageRef(c.Stage.Id, c.Stage.Name, c.Stage.Color, c.Stage.SystemKey),
                Treatments = c.Treatments.OrderBy(t => t.Treatment.DisplayOrder)
                    .Select(t => new NamedRef(t.TreatmentId, t.Treatment.Name)).ToList(),
                LeadSource = c.LeadSource != null ? c.LeadSource.Name : null,
                AssignedUser = c.AssignedUser != null ? c.AssignedUser.FullName : null,
                c.NextFollowUpDate,
                c.CreatedAt,
                Outstanding = db.Bookings.Where(b => b.CustomerId == c.Id).Sum(b => b.Balance),
                NextBooking = db.Bookings
                    .Where(b => b.CustomerId == c.Id && b.Status == BookingStatus.Booked && b.BookingDate >= today)
                    .OrderBy(b => b.BookingDate).ThenBy(b => b.StartTime)
                    .Select(b => new NextBookingDto(b.Id, b.BookingDate, b.StartTime))
                    .FirstOrDefault(),
            })
            .AsSplitQuery()
            .ToListAsync(ct);

        var consultations = await ConsultationsAsync(rows.Select(r => r.Id).ToList(), ct);
        var items = rows.Select(r => new CustomerListItemDto(
            r.Id, r.Name, Phone(r.WhatsAppNumber), r.InstagramName, r.Stage, consultations[r.Id], r.Treatments,
            r.LeadSource, r.AssignedUser, r.NextFollowUpDate, r.NextBooking, r.CreatedAt, r.Outstanding)).ToList();
        return new PagedResult<CustomerListItemDto>(items, page, size, total);
    }

    // ---- Consultation (worked out from bookings) -------------------------------------------------

    private static readonly BookingStatus[] Finished = [BookingStatus.Completed, BookingStatus.Cancelled, BookingStatus.NoShow];

    /// <summary>Each customer's consultation state. Rescheduled bookings are skipped: their replacement counts.</summary>
    private async Task<Dictionary<int, ConsultationDto>> ConsultationsAsync(IReadOnlyCollection<int> customerIds, CancellationToken ct)
    {
        var bookings = await db.Bookings.AsNoTracking()
            .Where(b => customerIds.Contains(b.CustomerId) && b.Status != BookingStatus.Rescheduled)
            .Select(b => new { b.CustomerId, b.Id, b.Status, b.BookingDate, b.StartTime, b.NextTreatmentDate, b.OriginalBookingId })
            .ToListAsync(ct);

        var result = new Dictionary<int, ConsultationDto>();
        foreach (var id in customerIds)
        {
            var mine = bookings.Where(b => b.CustomerId == id).ToList();
            var booked = mine.Where(b => b.Status == BookingStatus.Booked)
                .OrderBy(b => b.BookingDate).ThenBy(b => b.StartTime).ThenBy(b => b.Id).FirstOrDefault();
            var last = mine.Where(b => Finished.Contains(b.Status))
                .OrderByDescending(b => b.BookingDate).ThenByDescending(b => b.StartTime).ThenByDescending(b => b.Id).FirstOrDefault();

            result[id] = booked is not null
                ? new ConsultationDto(booked.OriginalBookingId is null ? ConsultationStates.Booked : ConsultationStates.Rescheduled,
                    booked.Id, booked.BookingDate, booked.StartTime, null)
                : last is null
                    ? new ConsultationDto(ConsultationStates.None, null, null, null, null)
                    : last.Status switch
                    {
                        BookingStatus.Completed => new ConsultationDto(ConsultationStates.Consulted, last.Id, last.BookingDate, last.StartTime, last.NextTreatmentDate),
                        BookingStatus.Cancelled => new ConsultationDto(ConsultationStates.Cancelled, last.Id, last.BookingDate, last.StartTime, null),
                        _ => new ConsultationDto(ConsultationStates.Missed, last.Id, last.BookingDate, last.StartTime, null),
                    };
        }
        return result;
    }

    /// <summary>The same rules as <see cref="ConsultationsAsync"/>, as a database filter.</summary>
    private IQueryable<Customer> FilterConsultation(IQueryable<Customer> query, string? state)
    {
        var bookings = db.Bookings;
        return state?.Trim().ToLowerInvariant() switch
        {
            // Booked vs rescheduled: whether the earliest booked consultation replaced a rescheduled one.
            ConsultationStates.Booked => query.Where(c => bookings.Where(b => b.CustomerId == c.Id && b.Status == BookingStatus.Booked)
                .OrderBy(b => b.BookingDate).ThenBy(b => b.StartTime).ThenBy(b => b.Id)
                .Select(b => (bool?)(b.OriginalBookingId == null)).FirstOrDefault() == true),
            ConsultationStates.Rescheduled => query.Where(c => bookings.Where(b => b.CustomerId == c.Id && b.Status == BookingStatus.Booked)
                .OrderBy(b => b.BookingDate).ThenBy(b => b.StartTime).ThenBy(b => b.Id)
                .Select(b => (bool?)(b.OriginalBookingId == null)).FirstOrDefault() == false),
            ConsultationStates.None => query.Where(c => !bookings.Any(b => b.CustomerId == c.Id && b.Status != BookingStatus.Rescheduled)),
            ConsultationStates.Consulted => LastFinished(query, BookingStatus.Completed),
            ConsultationStates.Cancelled => LastFinished(query, BookingStatus.Cancelled),
            ConsultationStates.Missed => LastFinished(query, BookingStatus.NoShow),
            _ => query,
        };
    }

    /// <summary>Customers with nothing booked whose latest finished consultation ended with <paramref name="status"/>.</summary>
    private IQueryable<Customer> LastFinished(IQueryable<Customer> query, BookingStatus status)
    {
        var bookings = db.Bookings;
        return query.Where(c =>
            !bookings.Any(b => b.CustomerId == c.Id && b.Status == BookingStatus.Booked)
            && bookings.Where(b => b.CustomerId == c.Id && Finished.Contains(b.Status))
                .OrderByDescending(b => b.BookingDate).ThenByDescending(b => b.StartTime).ThenByDescending(b => b.Id)
                .Select(b => (BookingStatus?)b.Status).FirstOrDefault() == status);
    }

    public async Task<CustomerDetailDto> GetAsync(int id, CancellationToken ct)
    {
        var c = await db.Customers.AsNoTracking()
            .Include(x => x.Stage)
            .Include(x => x.LeadSource)
            .Include(x => x.AssignedUser)
            .Include(x => x.Treatments).ThenInclude(t => t.Treatment)
            .Include(x => x.CustomFieldValues).ThenInclude(v => v.CustomField).ThenInclude(f => f.Options)
            .AsSplitQuery()
            .SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw BusinessRuleException.NotFound("Customer");

        var customFields = c.CustomFieldValues
            .Where(v => v.CustomField.IsActive)
            .OrderBy(v => v.CustomField.DisplayOrder)
            .Select(v =>
            {
                var (value, display) = CustomFieldValueConverter.FromStorage(v.CustomField, v.Value);
                return new CustomFieldValueDto(v.CustomFieldId, v.CustomField.Key, v.CustomField.Label,
                    v.CustomField.FieldType.ToString(), value, display);
            })
            .ToList();

        return new CustomerDetailDto(
            c.Id, c.Name, Phone(c.WhatsAppNumber), Phone(c.SecondaryPhone), c.InstagramName, c.Email,
            new StageRef(c.Stage.Id, c.Stage.Name, c.Stage.Color, c.Stage.SystemKey),
            (await ConsultationsAsync([c.Id], ct))[c.Id],
            c.LeadSource is null ? null : new NamedRef(c.LeadSource.Id, c.LeadSource.Name),
            c.AssignedUser is null ? null : new NamedRef(c.AssignedUser.Id, c.AssignedUser.FullName),
            c.LastContactDate, c.NextFollowUpDate, c.Notes, c.IsActive,
            c.Treatments.OrderBy(t => t.Treatment.DisplayOrder).Select(t => new NamedRef(t.TreatmentId, t.Treatment.Name)).ToList(),
            customFields, c.CreatedAt, c.UpdatedAt,
            await db.Bookings.Where(b => b.CustomerId == c.Id).SumAsync(b => b.Balance, ct));
    }

    public async Task<CustomerDetailDto> CreateAsync(SaveCustomerRequest request, int? userId, CancellationToken ct)
    {
        // A new customer is added for a treatment they're interested in.
        if (request.TreatmentIds is not { Count: > 0 })
            throw new BusinessRuleException("Choose at least one interested treatment.", field: "treatmentIds");

        var customer = new Customer();
        await ApplyAsync(customer, request, isNew: true, ct);
        db.Customers.Add(customer);
        await db.SaveChangesAsync(ct);

        audit.Record(userId, "Customer Created", nameof(Customer), customer.Id, new { customer.Name });
        await db.SaveChangesAsync(ct);
        return await GetAsync(customer.Id, ct);
    }

    public async Task<CustomerDetailDto> UpdateAsync(int id, SaveCustomerRequest request, int? userId, CancellationToken ct)
    {
        var customer = await db.Customers
            .Include(c => c.Treatments).ThenInclude(t => t.Treatment)
            .Include(c => c.CustomFieldValues)
            .Include(c => c.Stage)
            .SingleOrDefaultAsync(c => c.Id == id, ct)
            ?? throw BusinessRuleException.NotFound("Customer");

        var oldStage = customer.Stage;
        var oldTreatments = customer.Treatments.Select(t => t.TreatmentId).ToHashSet();

        // Interests can change but not be emptied. A customer who never had one (e.g. captured
        // without) can still be saved as is, so a quick stage change keeps working.
        if (oldTreatments.Count > 0 && request.TreatmentIds is not { Count: > 0 })
            throw new BusinessRuleException("Choose at least one interested treatment.", field: "treatmentIds");

        await ApplyAsync(customer, request, isNew: false, ct);

        // Stage and treatment changes get their own entries below, so they're left out here.
        var changed = db.Entry(customer).Properties
            .Where(p => p.IsModified && p.Metadata.Name is not (nameof(Customer.UpdatedAt) or nameof(Customer.StageId)))
            .Select(p => p.Metadata.Name).ToList();
        if (customer.CustomFieldValues.Any(v => db.Entry(v).State != EntityState.Unchanged)
            || db.ChangeTracker.Entries<CustomerCustomFieldValue>().Any(e => e.State == EntityState.Deleted))
            changed.Add("CustomFields");
        if (changed.Count > 0)
            audit.Record(userId, "Customer Updated", nameof(Customer), id, new { fields = changed });

        if (customer.StageId != oldStage.Id)
        {
            var newStage = await db.Stages.AsNoTracking().SingleAsync(s => s.Id == customer.StageId, ct);
            audit.Record(userId, "Stage Changed", nameof(Customer), id, new { from = oldStage.Name, to = newStage.Name });
        }

        var added = customer.Treatments.Where(t => !oldTreatments.Contains(t.TreatmentId)).Select(t => t.TreatmentId).ToList();
        if (added.Count > 0)
        {
            var names = await db.Treatments.Where(t => added.Contains(t.Id)).Select(t => t.Name).ToListAsync(ct);
            audit.Record(userId, "Treatment Added", nameof(Customer), id, new { treatments = names });
        }

        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<IReadOnlyList<ActivityDto>> ActivityAsync(int id, CancellationToken ct)
    {
        if (!await db.Customers.AnyAsync(c => c.Id == id, ct)) throw BusinessRuleException.NotFound("Customer");
        var key = id.ToString();
        var bookingKeys = await db.Bookings.Where(b => b.CustomerId == id).Select(b => b.Id.ToString()).ToListAsync(ct);
        var rows = await db.AuditLogs.AsNoTracking()
            .Where(a => (a.EntityType == nameof(Customer) && a.EntityId == key)
                        || (a.EntityType == nameof(Booking) && bookingKeys.Contains(a.EntityId!)))
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Take(100)
            .Select(a => new { a.Id, a.Action, UserName = a.User != null ? a.User.FullName : null, a.CreatedAt, a.Metadata })
            .ToListAsync(ct);

        return rows.Select(r => new ActivityDto(r.Id, r.Action, r.UserName, r.CreatedAt,
            r.Metadata is null ? null : JsonDocument.Parse(r.Metadata).RootElement.Clone())).ToList();
    }

    /// <summary>Validates the request and copies it onto the entity. Shared by create and update.</summary>
    private async Task ApplyAsync(Customer customer, SaveCustomerRequest r, bool isNew, CancellationToken ct)
    {
        // ---- Contact details, normalised; one identifier is required ----
        var whatsApp = contacts.NormalizePhone(r.WhatsApp);
        if (!string.IsNullOrWhiteSpace(r.WhatsApp) && whatsApp is null)
            throw new BusinessRuleException("Enter a valid WhatsApp number with its country code, e.g. +94 77 123 4567.", field: "whatsApp");

        var instagram = ContactNormalizer.NormalizeInstagram(r.Instagram);
        if (!string.IsNullOrWhiteSpace(r.Instagram) && instagram is null)
            throw new BusinessRuleException("Enter a valid Instagram name, e.g. @sarah.fernando.", field: "instagram");

        if (whatsApp is null && instagram is null)
            throw new BusinessRuleException("Add a WhatsApp number or an Instagram name.", field: "whatsApp");

        string? secondary = null;
        if (!string.IsNullOrWhiteSpace(r.SecondaryPhone))
        {
            secondary = contacts.NormalizePhone(r.SecondaryPhone)
                ?? throw new BusinessRuleException("Enter a valid secondary number with its country code.", field: "secondaryPhone");
        }

        await EnsureNotDuplicateAsync(whatsApp, instagram, customer.Id, ct);

        // ---- Lookups: new selections must be active; an existing inactive selection may stay ----
        int stageId;
        if (r.StageId is { } requestedStage)
        {
            var ok = await db.Stages.AnyAsync(s => s.Id == requestedStage && (s.IsActive || s.Id == customer.StageId), ct);
            stageId = ok ? requestedStage : throw new BusinessRuleException("Choose a valid stage.", field: "stageId");
        }
        else if (isNew)
        {
            // Stage automation (spec §18): a new customer starts as Interested.
            stageId = await db.Stages.Where(s => s.SystemKey == StageKeys.Interested).Select(s => s.Id).SingleAsync(ct);
        }
        else stageId = customer.StageId;

        if (r.LeadSourceId is { } sourceId
            && !await db.LeadSources.AnyAsync(s => s.Id == sourceId && (s.IsActive || s.Id == customer.LeadSourceId), ct))
            throw new BusinessRuleException("Choose a valid lead source.", field: "leadSourceId");

        if (r.AssignedUserId is { } assignee
            && !await db.Users.AnyAsync(u => u.Id == assignee && (u.IsActive || u.Id == customer.AssignedUserId), ct))
            throw new BusinessRuleException("Choose an active user to assign.", field: "assignedUserId");

        var treatmentIds = (r.TreatmentIds ?? []).Distinct().ToList();
        var current = customer.Treatments.Select(t => t.TreatmentId).ToHashSet();
        var validTreatments = await db.Treatments
            .Where(t => treatmentIds.Contains(t.Id) && (t.IsActive || current.Contains(t.Id)))
            .Select(t => t.Id).ToListAsync(ct);
        if (validTreatments.Count != treatmentIds.Count)
            throw new BusinessRuleException("One of the selected treatments is no longer available.", field: "treatmentIds");

        // ---- Copy ----
        customer.Name = r.Name.Trim();
        customer.WhatsAppNumber = whatsApp;
        customer.InstagramName = instagram;
        customer.SecondaryPhone = secondary;
        customer.Email = string.IsNullOrWhiteSpace(r.Email) ? null : r.Email.Trim().ToLowerInvariant();
        customer.StageId = stageId;
        customer.LeadSourceId = r.LeadSourceId;
        customer.AssignedUserId = r.AssignedUserId;
        customer.LastContactDate = r.LastContactDate;
        customer.NextFollowUpDate = r.NextFollowUpDate;
        customer.Notes = string.IsNullOrWhiteSpace(r.Notes) ? null : r.Notes.Trim();

        foreach (var removed in customer.Treatments.Where(t => !treatmentIds.Contains(t.TreatmentId)).ToList())
            customer.Treatments.Remove(removed);
        foreach (var id in treatmentIds.Where(id => !current.Contains(id)))
            customer.Treatments.Add(new CustomerTreatment { TreatmentId = id, CreatedAt = DateTime.UtcNow });

        await ApplyCustomFieldsAsync(customer, r.CustomFields ?? [], ct);
    }

    /// <summary>
    /// Active custom fields take the submitted values (required ones must be filled). Values of
    /// inactive fields are left untouched, so switching a field off never loses data.
    /// </summary>
    private async Task ApplyCustomFieldsAsync(Customer customer, Dictionary<string, JsonElement> values, CancellationToken ct)
    {
        var fields = await db.CustomFields.Include(f => f.Options).Where(f => f.IsActive).ToListAsync(ct);
        var now = DateTime.UtcNow;

        foreach (var field in fields)
        {
            var stored = values.TryGetValue(field.Key, out var raw) ? CustomFieldValueConverter.ToStorage(field, raw) : null;
            if (stored is null && field.IsRequired)
                throw new BusinessRuleException($"{field.Label} is required.", field: $"customFields.{field.Key}");

            var existing = customer.CustomFieldValues.FirstOrDefault(v => v.CustomFieldId == field.Id);
            if (stored is null)
            {
                if (existing is not null) customer.CustomFieldValues.Remove(existing);
            }
            else if (existing is null)
            {
                customer.CustomFieldValues.Add(new CustomerCustomFieldValue { CustomFieldId = field.Id, Value = stored, UpdatedAt = now });
            }
            else if (existing.Value != stored)
            {
                existing.Value = stored;
                existing.UpdatedAt = now;
            }
        }
    }

    private async Task EnsureNotDuplicateAsync(string? whatsApp, string? instagram, int selfId, CancellationToken ct)
    {
        if (whatsApp is not null)
        {
            var match = await db.Customers.AsNoTracking().Where(c => c.WhatsAppNumber == whatsApp && c.Id != selfId)
                .Select(c => new { c.Id, c.Name }).FirstOrDefaultAsync(ct);
            if (match is not null)
                throw new BusinessRuleException($"{match.Name} already has this WhatsApp number.", StatusCodes.Status409Conflict, "whatsApp")
                    { Details = new DuplicateCustomerDto(match.Id, match.Name, "whatsApp") };
        }
        if (instagram is not null)
        {
            var match = await db.Customers.AsNoTracking().Where(c => c.InstagramName == instagram && c.Id != selfId)
                .Select(c => new { c.Id, c.Name }).FirstOrDefaultAsync(ct);
            if (match is not null)
                throw new BusinessRuleException($"{match.Name} already has this Instagram name.", StatusCodes.Status409Conflict, "instagram")
                    { Details = new DuplicateCustomerDto(match.Id, match.Name, "instagram") };
        }
    }

    private static string? Phone(string? e164) => e164 is null ? null : ContactNormalizer.FormatPhone(e164);
}
