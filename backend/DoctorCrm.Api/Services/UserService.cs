using DoctorCrm.Api.Authorization;
using DoctorCrm.Api.Data;
using DoctorCrm.Api.DTOs;
using DoctorCrm.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace DoctorCrm.Api.Services;

/// <summary>
/// User administration (spec §44). Each user holds one role in the UI, although the schema
/// allows several. Two safety rules: nobody can deactivate themselves, and the last active
/// Admin can never be deactivated or demoted, so the system can't be locked.
/// </summary>
public class UserService(AppDbContext db, AuditService audit)
{
    public async Task<IReadOnlyList<RoleDto>> ListRolesAsync(CancellationToken ct) =>
        await db.Roles.AsNoTracking().OrderBy(r => r.Id)
            .Select(r => new RoleDto(r.Id, r.Name, r.Description))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<UserDto>> ListAsync(string? search, CancellationToken ct)
    {
        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(u => EF.Functions.ILike(u.FullName, term)
                                     || EF.Functions.ILike(u.Username, term)
                                     || (u.Email != null && EF.Functions.ILike(u.Email, term)));
        }

        return await query
            .OrderByDescending(u => u.IsActive).ThenBy(u => u.FullName)
            .Select(Projection)
            .ToListAsync(ct);
    }

    public async Task<UserDto> CreateAsync(CreateUserRequest request, int? actorId, CancellationToken ct)
    {
        var username = request.Username.Trim();
        await EnsureUsernameIsFreeAsync(username, null, ct);
        var role = await FindRoleAsync(request.RoleId, ct);

        var user = new User
        {
            FullName = request.FullName.Trim(),
            Username = username,
            NormalizedUsername = AuthService.NormalizeUsername(username),
            Email = NormalizeEmail(request.Email),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, workFactor: 12),
            MustChangePassword = true,
        };
        user.UserRoles.Add(new UserRole { Role = role });
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        audit.Record(actorId, "User Created", nameof(User), user.Id, new { user.Username, role = role.Name });
        await db.SaveChangesAsync(ct);
        return await GetAsync(user.Id, ct);
    }

    public async Task<UserDto> UpdateAsync(int id, UpdateUserRequest request, int? actorId, CancellationToken ct)
    {
        var user = await FindAsync(id, ct);
        await EnsureMayManageAsync(user, actorId, ct);
        var username = request.Username.Trim();
        await EnsureUsernameIsFreeAsync(username, id, ct);
        var role = await FindRoleAsync(request.RoleId, ct);

        var currentRole = user.UserRoles.FirstOrDefault()?.Role;
        if (currentRole?.Name == Roles.Admin && role.Name != Roles.Admin && user.IsActive)
            await EnsureAnotherActiveAdminAsync(id, "change the role of", ct);

        user.FullName = request.FullName.Trim();
        user.Username = username;
        user.NormalizedUsername = AuthService.NormalizeUsername(username);
        user.Email = NormalizeEmail(request.Email);

        if (currentRole?.Id != role.Id)
        {
            db.UserRoles.RemoveRange(user.UserRoles);
            user.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        }

        audit.Record(actorId, "User Updated", nameof(User), id,
            new { user.Username, role = role.Name, roleChanged = currentRole?.Id != role.Id });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<UserDto> SetActiveAsync(int id, bool isActive, int? actorId, CancellationToken ct)
    {
        var user = await FindAsync(id, ct);
        await EnsureMayManageAsync(user, actorId, ct);
        if (!isActive)
        {
            if (id == actorId)
                throw new BusinessRuleException("You can't deactivate your own account.");
            if (user.UserRoles.Any(ur => ur.Role.Name == Roles.Admin) && user.IsActive)
                await EnsureAnotherActiveAdminAsync(id, "deactivate", ct);
        }

        if (user.IsActive != isActive)
        {
            user.IsActive = isActive;
            audit.Record(actorId, isActive ? "User Activated" : "User Deactivated", nameof(User), id, new { user.Username });
            await db.SaveChangesAsync(ct);
        }
        return await GetAsync(id, ct);
    }

    public async Task ResetPasswordAsync(int id, string newPassword, int? actorId, CancellationToken ct)
    {
        var user = await FindAsync(id, ct);
        await EnsureMayManageAsync(user, actorId, ct);
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword, workFactor: 12);
        user.MustChangePassword = true;
        audit.Record(actorId, "Password Reset", nameof(User), id, new { user.Username });
        await db.SaveChangesAsync(ct);
    }

    private async Task<UserDto> GetAsync(int id, CancellationToken ct) =>
        await db.Users.AsNoTracking().Where(u => u.Id == id).Select(Projection).SingleAsync(ct);

    private static readonly System.Linq.Expressions.Expression<Func<User, UserDto>> Projection = u => new UserDto(
        u.Id,
        u.FullName,
        u.Username,
        u.Email,
        u.UserRoles.Select(ur => (int?)ur.RoleId).FirstOrDefault(),
        u.UserRoles.Select(ur => ur.Role.Name).FirstOrDefault(),
        u.IsActive,
        u.MustChangePassword,
        u.LastLoginAt,
        u.CreatedAt,
        u.IsPlatformOwner);

    /// <summary>
    /// A platform owner's account can only be changed by an owner (themselves included). Otherwise
    /// a clinic admin could reset an owner's password, sign in as them and switch billing off.
    /// </summary>
    private async Task EnsureMayManageAsync(User target, int? actorId, CancellationToken ct)
    {
        if (!target.IsPlatformOwner || target.Id == actorId) return;
        if (actorId is not null && await db.Users.AnyAsync(u => u.Id == actorId && u.IsPlatformOwner, ct)) return;
        throw new BusinessRuleException("This account belongs to a GrowDesk platform owner. Only a platform owner can change it.",
            StatusCodes.Status403Forbidden);
    }

    private async Task<User> FindAsync(int id, CancellationToken ct) =>
        await db.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role).SingleOrDefaultAsync(u => u.Id == id, ct)
        ?? throw BusinessRuleException.NotFound("User");

    private async Task<Role> FindRoleAsync(int roleId, CancellationToken ct) =>
        await db.Roles.SingleOrDefaultAsync(r => r.Id == roleId, ct)
        ?? throw new BusinessRuleException("Please choose a valid role.", field: "roleId");

    private async Task EnsureUsernameIsFreeAsync(string username, int? excludeId, CancellationToken ct)
    {
        var normalized = AuthService.NormalizeUsername(username);
        if (await db.Users.AnyAsync(u => u.NormalizedUsername == normalized && u.Id != excludeId, ct))
            throw BusinessRuleException.Conflict($"The username \"{username}\" is already taken.", "username");
    }

    private async Task EnsureAnotherActiveAdminAsync(int userId, string action, CancellationToken ct)
    {
        var others = await db.Users.CountAsync(u => u.Id != userId && u.IsActive
            && u.UserRoles.Any(ur => ur.Role.Name == Roles.Admin), ct);
        if (others == 0)
            throw new BusinessRuleException($"You can't {action} the last active administrator. Make another user an Admin first.");
    }

    private static string? NormalizeEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
}
