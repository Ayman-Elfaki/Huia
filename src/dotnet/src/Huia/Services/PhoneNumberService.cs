using System.Diagnostics.CodeAnalysis;
using PhoneNumbers;

namespace Huia.Services;

/// <summary>Normalizes and masks phone numbers for the passwordless SMS flow.</summary>
public interface IPhoneNumberService
{
    /// <summary>
    /// Parses <paramref name="input"/> (optionally using <paramref name="defaultRegion"/> for numbers
    /// without a country code) and, if it is a <em>possible</em> number, returns it in E.164 form.
    /// </summary>
    /// <param name="input">The raw user input.</param>
    /// <param name="defaultRegion">An ISO 3166-1 alpha-2 region for numbers with no country code.</param>
    /// <param name="e164">The normalized E.164 number on success.</param>
    /// <returns><see langword="true"/> when the number is usable.</returns>
    bool TryNormalize(string? input, string? defaultRegion, [NotNullWhen(true)] out string? e164);

    /// <summary>Masks an E.164 number to its last four digits (for logs and events).</summary>
    /// <param name="e164">The number.</param>
    /// <returns>A masked string such as <c>••••1234</c>.</returns>
    string Mask(string e164);
}

/// <summary>
/// libphonenumber-backed <see cref="IPhoneNumberService"/>. The gate is <c>IsPossibleNumber</c>, not
/// <c>IsValidNumber</c>: the latter rejects every Twilio magic <c>+1500…</c> test number and its
/// metadata changes month to month.
/// </summary>
public sealed class PhoneNumberService : IPhoneNumberService
{
    private static readonly PhoneNumberUtil Util = PhoneNumberUtil.GetInstance();

    /// <inheritdoc />
    public bool TryNormalize(string? input, string? defaultRegion, [NotNullWhen(true)] out string? e164)
    {
        e164 = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        try
        {
            var region = string.IsNullOrWhiteSpace(defaultRegion) ? null : defaultRegion.ToUpperInvariant();
            var parsed = Util.Parse(input, region);
            if (!Util.IsPossibleNumber(parsed))
            {
                return false;
            }

            e164 = Util.Format(parsed, PhoneNumberFormat.E164);
            return true;
        }
        catch (NumberParseException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public string Mask(string e164)
    {
        ArgumentException.ThrowIfNullOrEmpty(e164);
        var digits = new string([.. e164.Where(char.IsDigit)]);
        var lastFour = digits.Length <= 4 ? digits : digits[^4..];
        return "••••" + lastFour;
    }
}
