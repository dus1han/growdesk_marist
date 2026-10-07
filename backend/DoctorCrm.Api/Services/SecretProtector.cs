using System.Security.Cryptography;
using System.Text;
using DoctorCrm.Api.Authentication;
using Microsoft.Extensions.Options;

namespace DoctorCrm.Api.Services;

/// <summary>
/// Encrypts secrets stored in the database (the Stripe keys) with AES-GCM. The key is derived from
/// Jwt:Key, which lives in the server's environment, so a database backup alone can't reveal them.
/// Changing Jwt:Key makes saved secrets unreadable: they then have to be entered again.
/// </summary>
public class SecretProtector(IOptions<JwtOptions> jwt)
{
    private const string Version = "v1:";
    private readonly byte[] _key = HKDF.DeriveKey(HashAlgorithmName.SHA256,
        Encoding.UTF8.GetBytes(jwt.Value.Key), 32, info: Encoding.UTF8.GetBytes("growdesk.secret-protector"));

    public string Protect(string plaintext)
    {
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];
        using (var aes = new AesGcm(_key, tag.Length))
            aes.Encrypt(nonce, plain, cipher, tag);
        return Version + Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
    }

    /// <summary>The secret, or null when there is none or it can't be decrypted.</summary>
    public string? Unprotect(string? protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue) || !protectedValue.StartsWith(Version, StringComparison.Ordinal)) return null;
        try
        {
            var data = Convert.FromBase64String(protectedValue[Version.Length..]);
            var nonceSize = AesGcm.NonceByteSizes.MaxSize;
            var tagSize = AesGcm.TagByteSizes.MaxSize;
            var cipher = data.AsSpan(nonceSize + tagSize);
            var plain = new byte[cipher.Length];
            using var aes = new AesGcm(_key, tagSize);
            aes.Decrypt(data.AsSpan(0, nonceSize), cipher, data.AsSpan(nonceSize, tagSize), plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }
}
