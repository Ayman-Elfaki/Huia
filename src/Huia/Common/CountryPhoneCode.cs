namespace Huia.Common;

/// <summary>
/// A country/region offered by the phone sign-in country selector (see <see cref="CountryPhoneCodeProvider"/>).
/// </summary>
/// <param name="RegionCode">ISO 3166-1 alpha-2 code (e.g. <c>"US"</c>) — what <see cref="PhoneNumberValidator"/>
/// expects, and what flag-icons' <c>fi-xx</c> classes key on once lowercased.</param>
/// <param name="DialCode">The region's E.164 calling code, formatted with a leading <c>+</c> (e.g. <c>"+1"</c>).</param>
/// <param name="DisplayName">The region's name, localized to the current UI culture.</param>
public sealed record CountryPhoneCode(string RegionCode, string DialCode, string DisplayName);
