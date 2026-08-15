using PhoneNumbers;

namespace Huia.Common;

/// <summary>
/// Validates and normalizes a phone number before it's used to request or verify an OTP.
/// </summary>
internal static class PhoneNumberValidator
{
    private static readonly PhoneNumberUtil Util = PhoneNumberUtil.GetInstance();

    /// <summary>
    /// Returns <paramref name="rawInput"/> normalized to E.164 (e.g. <c>+15551234567</c>), or
    /// <see langword="null"/> if it isn't a valid, unambiguous phone number. No default region is applied —
    /// the client (via libphonenumber-js) is expected to submit an already <c>+</c>-prefixed international
    /// number; a non-<c>+</c>-prefixed input is rejected rather than guessed at, since guessing a region
    /// server-side risks normalizing what the user actually intended as two different numbers to the same
    /// string, or vice versa.
    /// </summary>
    public static string? TryNormalize(string? rawInput)
    {
        if (string.IsNullOrWhiteSpace(rawInput))
        {
            return null;
        }

        try
        {
            var parsed = Util.Parse(rawInput, null);
            return Util.IsValidNumber(parsed) ? Util.Format(parsed, PhoneNumberFormat.E164) : null;
        }
        catch (NumberParseException)
        {
            return null;
        }
    }
}
