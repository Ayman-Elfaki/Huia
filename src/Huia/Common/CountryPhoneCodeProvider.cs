using System.Globalization;
using PhoneNumbers;

namespace Huia.Common;

/// <summary>
/// Builds the country list for the phone sign-in country selector, from libphonenumber's own supported
/// regions — so it always matches exactly what <see cref="PhoneNumberValidator"/> will accept — paired with
/// each region's calling code and its <see cref="RegionInfo.DisplayName"/>. That's each region's own native
/// name (e.g. <c>"Deutschland"</c>, <c>"日本"</c>), not a translation into the current UI culture — .NET has
/// no reliable BCL API for the latter (RegionInfo.DisplayName doesn't actually vary with
/// <see cref="CultureInfo.CurrentUICulture"/>, despite its own docs implying otherwise), and native names are
/// themselves a legitimate, commonly-used choice for a country picker. Only the sort order uses the current
/// UI culture's collation, which is enough to keep the list stable and reasonably ordered even though the
/// names themselves aren't localized.
/// </summary>
internal static class CountryPhoneCodeProvider
{
    private static readonly PhoneNumberUtil Util = PhoneNumberUtil.GetInstance();

    /// <summary>Every region offered by the selector, sorted by localized display name.</summary>
    public static IReadOnlyList<CountryPhoneCode> GetAll()
    {
        var options = new List<CountryPhoneCode>();

        foreach (var region in Util.GetSupportedRegions())
        {
            RegionInfo info;
            try
            {
                info = new RegionInfo(region);
            }
            catch (ArgumentException)
            {
                // Not a real ISO 3166-1 region .NET recognizes (libphonenumber's supported regions include a
                // few of its own conventions) — skip rather than show a code with no name.
                continue;
            }

            var callingCode = Util.GetCountryCodeForRegion(region);
            if (callingCode <= 0)
            {
                continue;
            }

            options.Add(new CountryPhoneCode(region, "+" + callingCode.ToString(CultureInfo.InvariantCulture), info.DisplayName));
        }

        return options
            .OrderBy(c => c.DisplayName, StringComparer.Create(CultureInfo.CurrentUICulture, ignoreCase: true))
            .ToList();
    }

    /// <summary>Whether <paramref name="regionCode"/> is one of the regions <see cref="GetAll"/> offers —
    /// used to validate <c>UsePasswordlessFlow</c>'s <c>defaultCountryCode</c> option at startup.</summary>
    public static bool IsSupportedRegion(string regionCode) =>
        Util.GetSupportedRegions().Contains(regionCode.ToUpperInvariant());
}
