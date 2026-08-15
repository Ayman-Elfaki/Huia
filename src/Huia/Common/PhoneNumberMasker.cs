using System.Globalization;
using PhoneNumbers;

namespace Huia.Common;

/// <summary>
/// Masks a phone number for logging (e.g. <c>+15551234567</c> → <c>+1***-***-4567</c>) — country code and
/// last 4 digits stay visible, everything else is replaced. Every log statement anywhere in the passwordless
/// phone sign-in flow must route a phone number through this before it reaches an <c>ILogger</c> call.
/// </summary>
internal static class PhoneNumberMasker
{
    private static readonly PhoneNumberUtil Util = PhoneNumberUtil.GetInstance();

    public static string Mask(string phoneNumberE164)
    {
        if (string.IsNullOrEmpty(phoneNumberE164))
        {
            return phoneNumberE164;
        }

        try
        {
            var parsed = Util.Parse(phoneNumberE164, null);
            var countryCode = parsed.CountryCode.ToString(CultureInfo.InvariantCulture);
            var national = parsed.NationalNumber.ToString(CultureInfo.InvariantCulture);

            if (national.Length <= 4)
            {
                return $"+{countryCode}{new string('*', national.Length)}";
            }

            // Always two "***" groups regardless of the number's actual length (e.g. an 8-digit national
            // number masks the same shape as a 10-digit one) — this is a masked log value, not meant to be
            // parsed back into the original grouping, so a fixed, predictable shape is preferable to trying
            // to mirror each region's own formatting.
            var last4 = national[^4..];
            return $"+{countryCode}***-***-{last4}";
        }
        catch (NumberParseException)
        {
            // Masking must never throw — it sits on every phone-related log statement. Defensive fallback
            // for a value that isn't (or is no longer) a valid E.164 number.
            return phoneNumberE164.Length <= 5 ? "+***" : $"+***-***-{phoneNumberE164[^4..]}";
        }
    }
}
