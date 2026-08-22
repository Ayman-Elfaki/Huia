using PhoneNumbers;

namespace Huia.Common;

/// <summary>
/// Validates and normalizes a phone number, entered against an explicit country selection, before it's used
/// to request or verify an OTP.
/// </summary>
internal static class PhoneNumberValidator
{
    private static readonly PhoneNumberUtil Util = PhoneNumberUtil.GetInstance();

    /// <summary>
    /// Returns <paramref name="rawInput"/> normalized to E.164 (e.g. <c>+15551234567</c>), or
    /// <see langword="null"/> if it isn't a valid number for <paramref name="regionCode"/> (an ISO 3166-1
    /// alpha-2 code, e.g. <c>"US"</c>, as offered by <see cref="CountryPhoneCodeProvider"/>). The region comes
    /// from the caller's own explicit country selector rather than being guessed from the number itself, so a
    /// plain national-format number (e.g. <c>4155552671</c>) is expected here just as much as one already
    /// prefixed with <c>+</c> and a calling code — and if the number turns out to belong to a different
    /// country than the one selected (a pasted <c>+44...</c> number with <c>"US"</c> selected, say), that
    /// mismatch is exactly what makes this return <see langword="null"/>.
    /// </summary>
    public static string? TryNormalize(string? rawInput, string? regionCode)
    {
        if (string.IsNullOrWhiteSpace(rawInput) || string.IsNullOrWhiteSpace(regionCode))
        {
            return null;
        }

        try
        {
            var parsed = Util.Parse(rawInput, regionCode);
            return Util.IsValidNumberForRegion(parsed, regionCode) ? Util.Format(parsed, PhoneNumberFormat.E164) : null;
        }
        catch (NumberParseException)
        {
            return null;
        }
    }

    /// <summary>
    /// Returns <paramref name="rawInput"/> normalized to E.164, or <see langword="null"/> if it isn't a
    /// parseable, valid number. Unlike <see cref="TryNormalize"/>, this doesn't take (or validate against) an
    /// explicit region — it's for a caller that's expected to already supply a fully-qualified international
    /// number (leading <c>+</c> and country calling code), such as an admin typing a phone number directly
    /// rather than a country-selector-driven sign-in form.
    /// </summary>
    public static string? TryNormalizeE164(string? rawInput)
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
