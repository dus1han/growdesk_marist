using System.IdentityModel.Tokens.Jwt;
using DoctorCrm.Api.Authentication;
using DoctorCrm.Api.Authorization;
using DoctorCrm.Api.DTOs;
using DoctorCrm.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace DoctorCrm.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(AuthService auth, IOptions<AuthCookieOptions> cookieOptions) : ControllerBase
{
    private readonly AuthCookieOptions _cookie = cookieOptions.Value;

    /// <summary>Signs in and sets the HTTP-only session cookie.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task<ActionResult<ApiResponse<SessionDto>>> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await auth.LoginAsync(request, HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
        if (result is null)
            return Unauthorized(ApiResponse.Fail("Invalid username or password."));

        Response.Cookies.Append(_cookie.Name, result.Token, new CookieOptions
        {
            HttpOnly = true,
            Secure = _cookie.Secure,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = result.Session.ExpiresAt,
            IsEssential = true,
        });

        return Ok(ApiResponse<SessionDto>.Ok(result.Session));
    }

    /// <summary>Clears the session cookie.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<object>>> Logout(CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated == true && TryGetUserId(out var userId))
            await auth.RecordLogoutAsync(userId, ct);

        Response.Cookies.Delete(_cookie.Name, new CookieOptions { Path = "/", Secure = _cookie.Secure, SameSite = SameSiteMode.Lax });
        return Ok(ApiResponse.Ok("Signed out."));
    }

    /// <summary>Changes the signed-in user's password (also completes a required first-login change).</summary>
    [HttpPost("change-password")]
    [Authorize]
    [AllowWhilePasswordChangeRequired]
    [AllowWhileSubscriptionBlocked]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task<ActionResult<ApiResponse<SessionDto>>> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized(ApiResponse.Fail("Your session has expired. Please sign in again."));

        await auth.ChangePasswordAsync(userId, request, ct);
        return await Me(ct);
    }

    /// <summary>The signed-in user with roles, permissions and session expiry.</summary>
    [HttpGet("me")]
    [Authorize]
    [AllowWhilePasswordChangeRequired]
    [AllowWhileSubscriptionBlocked]
    public async Task<ActionResult<ApiResponse<SessionDto>>> Me(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized(ApiResponse.Fail("Your session has expired. Please sign in again."));

        var user = await auth.GetCurrentUserAsync(userId, ct);
        if (user is null)
            return Unauthorized(ApiResponse.Fail("Your session has expired. Please sign in again."));

        var exp = long.Parse(User.FindFirst(JwtRegisteredClaimNames.Exp)!.Value);
        return Ok(ApiResponse<SessionDto>.Ok(new SessionDto(user, DateTimeOffset.FromUnixTimeSeconds(exp).UtcDateTime)));
    }

    private bool TryGetUserId(out int userId) =>
        int.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out userId);
}

public static class RateLimitPolicies
{
    public const string Login = "login";
    public const string CaptureToken = "capture-token";
    public const string Capture = "capture";
}
