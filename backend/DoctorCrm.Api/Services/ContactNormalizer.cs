using System.Text.RegularExpressions;
using PhoneNumbers;

namespace DoctorCrm.Api.Services;

/// <summary>
/// Puts phone numbers and Instagram handles into one canonical form, so "050 123 4567",
/// "+971501234567" and "00971 50 123 4567" are recognised as the same customer. Used by both
/// the CRM forms and (Milestone 7) the capture API.
/// </summary>
public partial class ContactNormalizer(IConfiguration config)
{
    private static readonly PhoneNumberUtil Phones = PhoneNumberUtil.GetInstance();

    /// <summary>Region assumed for numbers typed without a country code. Phone:DefaultRegion, default AE.</summary>
    private readonly string _defaultRegion = config["Phone:DefaultRegion"] ?? "AE";

    [GeneratedRegex(@"^[a-z0-9._]{1,30}$")]
    private static partial Regex InstagramHandle();

    /// <summary>
    /// E.164, or null when the text is not a valid phone number. Any country is accepted:
    /// "+94 77…" and "0094 77…" are international; a number without a prefix is tried as a local
    /// number (default region) first, then as international digits ("94771234567").
    /// </summary>
    public string? NormalizePhone(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var text = input.Trim();
        if (text.StartsWith("00")) text = "+" + text[2..];

        var parsed = TryParse(text, _defaultRegion);
        if (parsed is null && !text.StartsWith('+'))
            parsed = TryParse("+" + new string(text.Where(char.IsDigit).ToArray()), null);
        return parsed ?? MexicanMobile(text);
    }

    /// <summary>
    /// Mexico dropped the "1" between +52 and mobile numbers in 2019, but WhatsApp still shows many
    /// as "+52 1 55 1234 5678", which is one digit too long to be valid. Stored without the 1, so
    /// both ways of writing the number find the same customer.
    /// </summary>
    private static string? MexicanMobile(string text)
    {
        var digits = new string(text.Where(char.IsDigit).ToArray());
        return digits.Length == 13 && digits.StartsWith("521") ? TryParse("+52" + digits[3..], null) : null;
    }

    private static string? TryParse(string text, string? region)
    {
        try
        {
            var number = Phones.Parse(text, region);
            return Phones.IsValidNumber(number) ? Phones.Format(number, PhoneNumberFormat.E164) : null;
        }
        catch (NumberParseException)
        {
            return null;
        }
    }

    /// <summary>"+971501234567" → "+971 50 123 4567". Falls back to the stored text.</summary>
    public static string FormatPhone(string? e164)
    {
        if (string.IsNullOrEmpty(e164)) return "";
        try
        {
            return Phones.Format(Phones.Parse(e164, null), PhoneNumberFormat.INTERNATIONAL);
        }
        catch (NumberParseException)
        {
            return e164;
        }
    }

    /// <summary>"@Sarah.F", "instagram.com/sarah.f/" → "sarah.f". Null when not a valid handle.</summary>
    public static string? NormalizeInstagram(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var text = input.Trim().ToLowerInvariant();

        var marker = text.IndexOf("instagram.com/", StringComparison.Ordinal);
        if (marker >= 0) text = text[(marker + "instagram.com/".Length)..];
        text = text.Split('?', '#')[0].Trim('/').TrimStart('@');

        return InstagramHandle().IsMatch(text) ? text : null;
    }
}
