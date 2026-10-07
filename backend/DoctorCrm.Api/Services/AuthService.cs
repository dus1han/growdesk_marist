using DoctorCrm.Api.Authentication;
using DoctorCrm.Api.Authorization;
using DoctorCrm.Api.Data;
using DoctorCrm.Api.DTOs;
using DoctorCrm.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace DoctorCrm.Api.Services;

public record LoginResult(string Token, SessionDto Session);

public class AuthService(AppDbContext db, TokenService tokens, AuditService audit)
{
    // Verified against when the username is unknown, so a missing account takes as long as a wrong password.
    private static readonly string DummyHash = BCrypt.Net.BCrypt.HashPassword("dummy-password-for-timing", workFactor: 12);

    /// <summary>Returns null for any failure. The caller shows one generic message, never which part was wrong.</summary>
    public async Task<LoginResult?> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken ct)
    {
        var normalized = NormalizeUsername(request.Username);
        var user = await db.Users.SingleOrDefaultAsync(u => u.NormalizedUsername == normalized, ct);

        var passwordOk = BCrypt.Net.BCrypt.Verify(request.Password, user?.PasswordHash ?? DummyHash);
        if (user is null || !passwordOk || !user.IsActive)
        {
            audit.Record(user?.Id, AuditActions.UserLoginFailed, nameof(User), user?.Id,
                new { username = request.Username.Trim(), ipAddress, reason = user is null ? "unknown_username" : !passwordOk ? "bad_password" : "inactive" });
            await db.SaveChangesAsync(ct);
            return null;
        }

        user.LastLoginAt = DateTime.UtcNow;
        audit.Record(user.Id, AuditActions.UserLoggedIn, nameof(User), user.Id, new { ipAddress });
        await db.SaveChangesAsync(ct);

        var current = await GetCurrentUserAsync(user.Id, ct)
            ?? throw new InvalidOperationException("User vanished during login.");
        var (token, expiresAt) = tokens.CreateToken(user.Id, user.Username, user.FullName, current.Roles, current.Permissions);

        return new LoginResult(token, new SessionDto(current, expiresAt));
    }

    /// <summary>Loads the user fresh from the database, so role or status changes apply immediately.</summary>
    public async Task<CurrentUserDto?> GetCurrentUserAsync(int userId, CancellationToken ct)
    {
        var user = await db.Users
            .AsNoTracking()
            .AsSplitQuery()
            .Where(u => u.Id == userId && u.IsActive)
            .Select(u => new
            {
                u.Id,
                u.FullName,
                u.Username,
                u.Email,
                u.MustChangePassword,
                u.IsPlatformOwner,
                Roles = u.UserRoles.Select(ur => ur.Role.Name).ToList(),
                Permissions = u.UserRoles
                    .SelectMany(ur => ur.Role.RolePermissions)
                    .Select(rp => rp.Permission.Key)
                    .Distinct()
                    .ToList(),
            })
            .SingleOrDefaultAsync(ct);

        if (user is null) return null;
        var permissions = user.IsPlatformOwner ? user.Permissions.Append(Permissions.PlatformBilling) : user.Permissions;
        return new CurrentUserDto(user.Id, user.FullName, user.Username, user.Email, user.Roles, permissions.Order().ToList(), user.MustChangePassword);
    }

    /// <summary>Usernames are unique regardless of case: "dev_admin" and "Dev_Admin" are the same account.</summary>
    public static string NormalizeUsername(string username) => username.Trim().ToUpperInvariant();

    /// <summary>
    /// Changes the signed-in user's password. The current password is always required, so a
    /// session left open on a shared computer can't be used to take over the account.
    /// </summary>
    public async Task ChangePasswordAsync(int userId, ChangePasswordRequest request, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId && u.IsActive, ct)
            ?? throw BusinessRuleException.NotFound("User");

        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
            throw new BusinessRuleException("Your current password is incorrect.", field: "currentPassword");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword, workFactor: 12);
        user.MustChangePassword = false;
        audit.Record(userId, AuditActions.PasswordChanged, nameof(User), userId);
        await db.SaveChangesAsync(ct);
    }

    public async Task RecordLogoutAsync(int userId, CancellationToken ct)
    {
        audit.Record(userId, AuditActions.UserLoggedOut, nameof(User), userId);
        await db.SaveChangesAsync(ct);
    }
}
