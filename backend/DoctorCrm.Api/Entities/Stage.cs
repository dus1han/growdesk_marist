namespace DoctorCrm.Api.Entities;

/// <summary>
/// A customer's status (shown as "Status"; called a stage in code and the database): the clinic's
/// own judgement, set by staff. Where a customer is with consultations is not a status: it is
/// worked out from their bookings (see ConsultationDto).
/// </summary>
public class Stage : AuditableEntity, ILookupEntity, IHasColor, ISoftDeletable
{
    public int Id { get; set; }

    /// <summary>Set when deleted to the recycle bin (<see cref="ISoftDeletable"/>).</summary>
    public DateTime? DeletedAt { get; set; }
    public int? DeletedById { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Fixed key the app relies on (e.g. "interested", "customer"). Admins can rename
    /// <see cref="Name"/> freely; automation always looks stages up by this key. Null for
    /// stages added by admins, which automation never targets.
    /// </summary>
    public string? SystemKey { get; set; }

    /// <summary>Hex colour, e.g. "#6366F1".</summary>
    public string Color { get; set; } = "#64748B";

    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public static class StageKeys
{
    /// <summary>Where new leads start (capture toolbar, WhatsApp BOT, a new customer without a status).</summary>
    public const string Interested = "interested";
    public const string FollowUp = "follow_up";
    /// <summary>Set automatically when a consultation is completed, the only automatic status change.</summary>
    public const string Customer = "customer";
    public const string Lost = "lost";
}
