namespace DoctorCrm.Api.Entities;

/// <summary>A potential or existing patient (spec §14, §39). Never deleted; see <see cref="IsActive"/>.</summary>
public class Customer : AuditableEntity, ISoftDeletable
{
    public int Id { get; set; }

    /// <summary>Set when deleted to the recycle bin (<see cref="ISoftDeletable"/>).</summary>
    public DateTime? DeletedAt { get; set; }
    public int? DeletedById { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>E.164 (e.g. "+971501234567"). Unique when present: the primary duplicate check.</summary>
    public string? WhatsAppNumber { get; set; }

    /// <summary>E.164 when it parses as a phone number, otherwise as entered.</summary>
    public string? SecondaryPhone { get; set; }

    /// <summary>Lower-case handle without "@" or URL. Unique when present: the fallback duplicate check.</summary>
    public string? InstagramName { get; set; }

    public string? Email { get; set; }

    public int StageId { get; set; }
    public Stage Stage { get; set; } = null!;

    public int? AssignedUserId { get; set; }
    public User? AssignedUser { get; set; }

    public int? LeadSourceId { get; set; }
    public LeadSource? LeadSource { get; set; }

    public DateOnly? LastContactDate { get; set; }
    public DateOnly? NextFollowUpDate { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<CustomerTreatment> Treatments { get; set; } = new List<CustomerTreatment>();
    public ICollection<CustomerCustomFieldValue> CustomFieldValues { get; set; } = new List<CustomerCustomFieldValue>();
}

/// <summary>A treatment the customer is interested in (spec §15). Many-to-many, never a text field.</summary>
public class CustomerTreatment
{
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public int TreatmentId { get; set; }
    public Treatment Treatment { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// One custom field's value for one customer, stored as text: numbers in invariant format,
/// dates as yyyy-MM-dd, dropdowns as the option id, multi-selects as a JSON array of option ids,
/// booleans as "true"/"false". <see cref="Services.CustomFieldValueConverter"/> owns the format.
/// </summary>
public class CustomerCustomFieldValue
{
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public int CustomFieldId { get; set; }
    public CustomField CustomField { get; set; } = null!;
    public string Value { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}
