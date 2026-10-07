namespace DoctorCrm.Api.Entities;

public class User : AuditableEntity
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;

    /// <summary>Sign-in name, stored as entered (e.g. "Dev_Admin").</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Upper-cased <see cref="Username"/>; the unique, case-insensitive lookup key.</summary>
    public string NormalizedUsername { get; set; } = string.Empty;

    /// <summary>Optional contact email. Not used for sign-in.</summary>
    public string? Email { get; set; }

    public string PasswordHash { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// Set when an admin creates the account or resets its password; the user must choose their
    /// own password before using the app. Cleared by a successful password change.
    /// </summary>
    public bool MustChangePassword { get; set; }

    /// <summary>
    /// Runs the GrowDesk platform for this clinic (e.g. the vendor's own staff): may configure
    /// Stripe billing. Not a role, and never granted from the app: set by migration or seed only.
    /// Only another owner can edit, reset or deactivate an owner's account.
    /// </summary>
    public bool IsPlatformOwner { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}
