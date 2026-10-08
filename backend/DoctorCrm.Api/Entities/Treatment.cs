namespace DoctorCrm.Api.Entities;

public class Treatment : AuditableEntity, ILookupEntity, IHasDescription, ISoftDeletable
{
    public int Id { get; set; }

    /// <summary>Set when deleted to the recycle bin (<see cref="ISoftDeletable"/>).</summary>
    public DateTime? DeletedAt { get; set; }
    public int? DeletedById { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
}
