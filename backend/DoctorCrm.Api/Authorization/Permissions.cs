namespace DoctorCrm.Api.Authorization;

/// <summary>
/// The single list of permission keys. The frontend receives the current user's keys from
/// /api/auth/me and never hard-codes role checks.
/// </summary>
public static class Permissions
{
    public const string DashboardView = "dashboard.view";

    public const string CustomersView = "customers.view";
    public const string CustomersManage = "customers.manage";

    public const string BookingsView = "bookings.view";
    public const string BookingsManage = "bookings.manage";
    public const string BookingsComplete = "bookings.complete";

    public const string PaymentsView = "payments.view";
    public const string PaymentsManage = "payments.manage";

    public const string AdminAccess = "admin.access";
    public const string UsersManage = "admin.users";
    public const string SettingsManage = "admin.settings";
    public const string AuditView = "admin.audit";
    public const string BillingManage = "admin.billing";

    public static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>
    {
        [DashboardView] = "View the dashboard",
        [CustomersView] = "View customers",
        [CustomersManage] = "Create and edit customers",
        [BookingsView] = "View bookings and the calendar",
        [BookingsManage] = "Create, reschedule and cancel bookings",
        [BookingsComplete] = "Complete consultations",
        [PaymentsView] = "View payments",
        [PaymentsManage] = "Record payments",
        [AdminAccess] = "Open the administration area",
        [UsersManage] = "Manage users",
        [SettingsManage] = "Manage treatments, stages, fields and settings",
        [AuditView] = "View the audit log",
        [BillingManage] = "Manage and pay the GrowDesk subscription",
    };

    public static IEnumerable<string> All => Descriptions.Keys;

    /// <summary>
    /// Configure Stripe billing. Deliberately not a role permission (so the clinic's Admin role,
    /// which receives every permission above, never gets it): held only by platform owners
    /// (<see cref="Entities.User.IsPlatformOwner"/>) and added to their session at sign-in.
    /// </summary>
    public const string PlatformBilling = "platform.billing";
}

public static class Roles
{
    public const string Admin = "Admin";
    public const string Doctor = "Doctor";
    public const string Receptionist = "Receptionist";
    public const string Staff = "Staff";

    /// <summary>Default permission set for each built-in role, applied by the seeder.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> DefaultPermissions = new Dictionary<string, string[]>
    {
        [Admin] = Permissions.All.ToArray(),
        [Doctor] =
        [
            Permissions.DashboardView, Permissions.CustomersView, Permissions.CustomersManage,
            Permissions.BookingsView, Permissions.BookingsManage, Permissions.BookingsComplete,
            Permissions.PaymentsView, Permissions.PaymentsManage,
        ],
        [Receptionist] =
        [
            Permissions.DashboardView, Permissions.CustomersView, Permissions.CustomersManage,
            Permissions.BookingsView, Permissions.BookingsManage, Permissions.BookingsComplete,
            Permissions.PaymentsView, Permissions.PaymentsManage,
        ],
        [Staff] =
        [
            Permissions.DashboardView, Permissions.CustomersView, Permissions.BookingsView,
            Permissions.BookingsComplete,
        ],
    };
}
