using DoctorCrm.Api.Authorization;
using DoctorCrm.Api.Entities;
using DoctorCrm.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace DoctorCrm.Api.Data;

/// <summary>
/// Idempotent seed data: roles, permissions, default stages, starter treatments, settings and
/// the first admin. Safe to run on every start: it only adds what is missing and never
/// overwrites values an admin has changed.
/// </summary>
public class DbSeeder(AppDbContext db, IConfiguration config, ILogger<DbSeeder> logger)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        await SeedPermissionsAndRolesAsync(ct);
        await SeedStagesAsync(ct);
        await SeedTreatmentsAsync(ct);
        await SeedListAsync(db.LeadSources, ["Instagram", "WhatsApp", "Facebook", "Website", "Referral", "Walk-in"], ct);
        await EnsureLeadSourceAsync(BotService.SourceName, ct);
        await SeedListAsync(db.CancellationReasons,
            ["Customer request", "Booked elsewhere", "No longer interested", "Doctor unavailable", "Other"], ct);
        await SeedListAsync(db.PaymentMethods, ["Cash", "Card", "Bank Transfer", "Other"], ct);
        await SeedCaptureFieldsAsync(ct);
        await SeedSettingsAsync(ct);
        await SeedAdminAsync(ct);
    }

    private async Task SeedPermissionsAndRolesAsync(CancellationToken ct)
    {
        var perms = await db.Permissions.ToDictionaryAsync(p => p.Key, ct);
        foreach (var (key, description) in Permissions.Descriptions)
        {
            if (perms.ContainsKey(key)) continue;
            var p = new Permission { Key = key, Description = description };
            db.Permissions.Add(p);
            perms[key] = p;
        }

        var roles = await db.Roles.Include(r => r.RolePermissions).ToDictionaryAsync(r => r.Name, ct);
        foreach (var (roleName, permKeys) in Roles.DefaultPermissions)
        {
            // Only a brand-new role gets its default set, so later admin edits survive restarts.
            if (roles.ContainsKey(roleName)) continue;

            var role = new Role { Name = roleName };
            foreach (var key in permKeys)
                role.RolePermissions.Add(new RolePermission { Permission = perms[key] });
            db.Roles.Add(role);
        }

        // Admin always holds every permission, including ones added in later releases.
        if (roles.TryGetValue(Roles.Admin, out var admin))
        {
            var held = admin.RolePermissions.Select(rp => rp.PermissionId).ToHashSet();
            foreach (var p in perms.Values.Where(p => p.Id == 0 || !held.Contains(p.Id)))
                admin.RolePermissions.Add(new RolePermission { Permission = p });
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task SeedStagesAsync(CancellationToken ct)
    {
        if (await db.Stages.AnyAsync(ct)) return;

        (string Name, string Key, string Color)[] defaults =
        [
            ("Interested", StageKeys.Interested, "#6366F1"),
            ("Follow-up", StageKeys.FollowUp, "#F59E0B"),
            ("Customer", StageKeys.Customer, "#22C55E"),
            ("Lost", StageKeys.Lost, "#94A3B8"),
        ];

        for (var i = 0; i < defaults.Length; i++)
        {
            var (name, key, color) = defaults[i];
            db.Stages.Add(new Stage { Name = name, SystemKey = key, Color = color, DisplayOrder = i + 1 });
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task SeedTreatmentsAsync(CancellationToken ct)
    {
        if (await db.Treatments.AnyAsync(ct)) return;

        string[] names = ["Botox", "Dermal Filler", "Hair Treatment", "Skin Treatment", "Laser"];
        for (var i = 0; i < names.Length; i++)
            db.Treatments.Add(new Treatment { Name = names[i], DisplayOrder = i + 1 });

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Seeds a simple list only when it is empty, so admin edits are never overwritten.</summary>
    private async Task SeedListAsync<T>(DbSet<T> set, string[] names, CancellationToken ct) where T : class, ILookupEntity, new()
    {
        if (await set.AnyAsync(ct)) return;
        for (var i = 0; i < names.Length; i++)
            set.Add(new T { Name = names[i], IsActive = true, DisplayOrder = i + 1 });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Adds a lead source the app relies on (once; an admin may rename or deactivate it later).</summary>
    private async Task EnsureLeadSourceAsync(string name, CancellationToken ct)
    {
        if (await db.LeadSources.AnyAsync(s => s.Name == name, ct)) return;
        var order = await db.LeadSources.MaxAsync(s => (int?)s.DisplayOrder, ct) ?? 0;
        db.LeadSources.Add(new LeadSource { Name = name, IsActive = true, DisplayOrder = order + 1 });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Adds a row for any built-in capture field that doesn't have one yet.</summary>
    private async Task SeedCaptureFieldsAsync(CancellationToken ct)
    {
        var existing = await db.CaptureFieldConfigurations.Select(c => c.FieldKey).ToListAsync(ct);
        var order = await db.CaptureFieldConfigurations.MaxAsync(c => (int?)c.DisplayOrder, ct) ?? 0;
        foreach (var f in CaptureFields.BuiltIns.Where(b => !existing.Contains(b.Key)))
        {
            db.CaptureFieldConfigurations.Add(new CaptureFieldConfiguration
            {
                FieldKey = f.Key,
                IsEnabled = f.DefaultEnabled,
                IsRequired = f.DefaultRequired,
                DisplayOrder = ++order,
                UpdatedAt = DateTime.UtcNow,
            });
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedSettingsAsync(CancellationToken ct)
    {
        var defaults = new Dictionary<string, string>
        {
            [SettingKeys.CrmName] = "GrowDesk",
            [SettingKeys.Tagline] = "Every patient relationship, beautifully organised.",
            [SettingKeys.LogoUrl] = "",
            [SettingKeys.Currency] = "AED",
            [SettingKeys.TimeZone] = "Asia/Dubai",
            [SettingKeys.OpeningHours] = BookingHoursService.DefaultDaysJson,
            [SettingKeys.BotBookingMinutes] = BookingHoursService.DefaultBotMinutes.ToString(),
        };

        var existing = await db.SystemSettings.Select(s => s.Key).ToListAsync(ct);
        foreach (var (key, value) in defaults.Where(d => !existing.Contains(d.Key)))
            db.SystemSettings.Add(new SystemSetting { Key = key, Value = value, UpdatedAt = DateTime.UtcNow });

        await db.SaveChangesAsync(ct);
    }

    private async Task SeedAdminAsync(CancellationToken ct)
    {
        if (await db.Users.AnyAsync(ct)) return;

        var username = config["Seed:AdminUsername"]?.Trim();
        var password = config["Seed:AdminPassword"];
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("No users exist and Seed:AdminUsername / Seed:AdminPassword are not set, so nobody can log in yet.");
            return;
        }

        var adminRole = await db.Roles.SingleAsync(r => r.Name == Roles.Admin, ct);
        var user = new User
        {
            FullName = config["Seed:AdminName"] ?? "Administrator",
            Username = username,
            NormalizedUsername = AuthService.NormalizeUsername(username),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12),
            // Whoever deploys GrowDesk runs the platform: the first admin may configure billing.
            IsPlatformOwner = true,
        };
        user.UserRoles.Add(new UserRole { Role = adminRole });
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Seeded first admin user {Username}", username);
    }
}
