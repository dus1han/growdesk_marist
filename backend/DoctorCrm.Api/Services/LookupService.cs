using DoctorCrm.Api.Data;
using DoctorCrm.Api.DTOs;
using DoctorCrm.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace DoctorCrm.Api.Services;

/// <summary>
/// List, create, edit, activate/deactivate and reorder for every admin-managed list. Items are
/// never deleted: inactive items disappear from new forms but stay on historical records.
/// </summary>
public class LookupService<T>(AppDbContext db, AuditService audit) where T : class, ILookupEntity, new()
{
    private const string DefaultStageColor = "#64748B";

    /// <summary>Human name for messages and the audit log, e.g. "Lead source".</summary>
    public static string DisplayName => typeof(T).Name switch
    {
        nameof(LeadSource) => "Lead source",
        nameof(CancellationReason) => "Cancellation reason",
        nameof(PaymentMethod) => "Payment method",
        var n => n,
    };

    public async Task<IReadOnlyList<LookupItemDto>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        var items = await db.Set<T>().AsNoTracking()
            .Where(x => includeInactive || x.IsActive)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
            .ToListAsync(ct);
        return items.Select(ToDto).ToList();
    }

    public async Task<LookupItemDto> CreateAsync(SaveLookupItemRequest request, int? userId, CancellationToken ct)
    {
        var name = request.Name.Trim();
        await EnsureNameIsFreeAsync(name, excludeId: null, ct);

        var maxOrder = await db.Set<T>().MaxAsync(x => (int?)x.DisplayOrder, ct) ?? 0;
        var item = new T { Name = name, IsActive = true, DisplayOrder = maxOrder + 1 };
        Apply(item, request);

        db.Set<T>().Add(item);
        await db.SaveChangesAsync(ct);
        audit.Record(userId, $"{DisplayName} Created", typeof(T).Name, item.Id, new { item.Name });
        await db.SaveChangesAsync(ct);
        return ToDto(item);
    }

    public async Task<LookupItemDto> UpdateAsync(int id, SaveLookupItemRequest request, int? userId, CancellationToken ct)
    {
        var item = await FindAsync(id, ct);
        var name = request.Name.Trim();
        await EnsureNameIsFreeAsync(name, id, ct);

        var before = item.Name;
        item.Name = name;
        Apply(item, request);
        audit.Record(userId, $"{DisplayName} Updated", typeof(T).Name, id, new { from = before, to = name });
        await db.SaveChangesAsync(ct);
        return ToDto(item);
    }

    public async Task<LookupItemDto> SetActiveAsync(int id, bool isActive, int? userId, CancellationToken ct)
    {
        var item = await FindAsync(id, ct);

        // The app looks built-in statuses up by key (new leads, completed consultations); switching one off would break that.
        if (!isActive && SystemKeyOf(item) is not null)
            throw new BusinessRuleException(
                $"\"{item.Name}\" is built in and can't be deactivated. You can rename it instead.");

        if (item.IsActive != isActive)
        {
            item.IsActive = isActive;
            audit.Record(userId, $"{DisplayName} {(isActive ? "Activated" : "Deactivated")}", typeof(T).Name, id, new { item.Name });
            await db.SaveChangesAsync(ct);
        }
        return ToDto(item);
    }

    /// <summary>Applies a new order. The ids must be exactly the full set of items.</summary>
    public async Task<IReadOnlyList<LookupItemDto>> ReorderAsync(IReadOnlyList<int> ids, int? userId, CancellationToken ct)
    {
        var items = await db.Set<T>().ToListAsync(ct);
        if (items.Count != ids.Count || items.Any(i => !ids.Contains(i.Id)))
            throw new BusinessRuleException("The list changed while you were reordering it. Please refresh and try again.",
                StatusCodes.Status409Conflict);

        var position = ids.Select((id, index) => (id, index)).ToDictionary(p => p.id, p => p.index + 1);
        foreach (var item in items) item.DisplayOrder = position[item.Id];

        audit.Record(userId, $"{DisplayName} Reordered", typeof(T).Name, null, new { ids });
        await db.SaveChangesAsync(ct);
        return items.OrderBy(i => i.DisplayOrder).Select(ToDto).ToList();
    }

    private async Task<T> FindAsync(int id, CancellationToken ct) =>
        await db.Set<T>().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw BusinessRuleException.NotFound(DisplayName);

    private async Task EnsureNameIsFreeAsync(string name, int? excludeId, CancellationToken ct)
    {
        var lower = name.ToLower();
        if (await db.Set<T>().AnyAsync(x => x.Name.ToLower() == lower && x.Id != excludeId, ct))
            throw BusinessRuleException.Conflict($"A {DisplayName.ToLower()} named \"{name}\" already exists.", "name");
    }

    private static void Apply(T item, SaveLookupItemRequest request)
    {
        if (item is IHasDescription d)
            d.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        if (item is IHasColor c)
            c.Color = (request.Color ?? (string.IsNullOrEmpty(c.Color) ? DefaultStageColor : c.Color)).ToUpperInvariant();
    }

    /// <summary>Built-in statuses and lead sources carry a key the app relies on.</summary>
    private static string? SystemKeyOf(T x) => (x as Stage)?.SystemKey ?? (x as LeadSource)?.SystemKey;

    private static LookupItemDto ToDto(T x) => new(
        x.Id,
        x.Name,
        (x as IHasDescription)?.Description,
        (x as IHasColor)?.Color,
        SystemKeyOf(x),
        x.IsActive,
        x.DisplayOrder);
}
