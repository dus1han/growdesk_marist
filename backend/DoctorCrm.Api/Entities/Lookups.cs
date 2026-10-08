namespace DoctorCrm.Api.Entities;

/// <summary>
/// An admin-managed list item: treatments, stages, lead sources, cancellation reasons and
/// payment methods. Items are deactivated when no longer offered; an item nothing uses any more
/// can also be deleted to the recycle bin.
/// </summary>
public interface ILookupEntity
{
    int Id { get; set; }
    string Name { get; set; }
    bool IsActive { get; set; }
    int DisplayOrder { get; set; }
}

public interface IHasDescription
{
    string? Description { get; set; }
}

public interface IHasColor
{
    string Color { get; set; }
}

public class LeadSource : AuditableEntity, ILookupEntity, ISoftDeletable
{
    public int Id { get; set; }

    /// <summary>Set when deleted to the recycle bin (<see cref="ISoftDeletable"/>).</summary>
    public DateTime? DeletedAt { get; set; }
    public int? DeletedById { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }

    /// <summary>
    /// Set on the sources the capture toolbar assigns by itself (<see cref="LeadSourceKeys"/>). The
    /// app finds them by key, so they can be renamed but not deactivated.
    /// </summary>
    public string? SystemKey { get; set; }
}

/// <summary>Built-in lead sources: the sites the GrowDesk Capture toolbar works on.</summary>
public static class LeadSourceKeys
{
    public const string WhatsApp = "whatsapp";
    public const string Instagram = "instagram";

    /// <summary>Key and the name a new clinic starts with.</summary>
    public static readonly (string Key, string Name)[] All = [(WhatsApp, "WhatsApp"), (Instagram, "Instagram")];
}

public class CancellationReason : AuditableEntity, ILookupEntity, ISoftDeletable
{
    public int Id { get; set; }

    /// <summary>Set when deleted to the recycle bin (<see cref="ISoftDeletable"/>).</summary>
    public DateTime? DeletedAt { get; set; }
    public int? DeletedById { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
}

public class PaymentMethod : AuditableEntity, ILookupEntity, ISoftDeletable
{
    public int Id { get; set; }

    /// <summary>Set when deleted to the recycle bin (<see cref="ISoftDeletable"/>).</summary>
    public DateTime? DeletedAt { get; set; }
    public int? DeletedById { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
}
