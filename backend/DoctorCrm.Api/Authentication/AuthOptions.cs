using System.ComponentModel.DataAnnotations;

namespace DoctorCrm.Api.Authentication;

public class JwtOptions
{
    public const string Section = "Jwt";

    /// <summary>HMAC signing key. At least 32 characters; never committed.</summary>
    [Required, MinLength(32)]
    public string Key { get; set; } = string.Empty;

    [Required]
    public string Issuer { get; set; } = "growdesk";

    [Required]
    public string Audience { get; set; } = "growdesk";

    [Range(15, 24 * 60)]
    public int SessionMinutes { get; set; } = 480;
}

public class AuthCookieOptions
{
    public const string Section = "AuthCookie";

    public string Name { get; set; } = "growdesk_marist_session";

    /// <summary>
    /// Send the cookie only over HTTPS. True in production behind TLS; must be false while the
    /// CRM is served over plain HTTP on ip:port, or the browser silently drops the cookie.
    /// </summary>
    public bool Secure { get; set; } = true;
}
