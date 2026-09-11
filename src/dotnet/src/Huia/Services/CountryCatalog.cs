using System.Collections.Concurrent;
using System.Globalization;
using PhoneNumbers;

namespace Huia.Services;

/// <summary>A country in the phone-number picker: ISO code, localized name and dialling code.</summary>
/// <param name="RegionCode">ISO 3166-1 alpha-2 code.</param>
/// <param name="DisplayName">Localized country name.</param>
/// <param name="DialCode">International dialling code (without the leading +).</param>
public sealed record CountryDialInfo(string RegionCode, string DisplayName, int DialCode);

/// <summary>Supplies the country list for the phone sign-in picker, localized to the current UI culture.</summary>
public interface ICountryCatalog
{
    /// <summary>The supported countries, sorted by localized name for the current UI culture.</summary>
    /// <returns>The country list.</returns>
    IReadOnlyList<CountryDialInfo> GetCountries();
}

/// <summary>libphonenumber-backed <see cref="ICountryCatalog"/> with a per-culture cache.</summary>
public sealed class CountryCatalog : ICountryCatalog
{
    private static readonly PhoneNumberUtil Util = PhoneNumberUtil.GetInstance();
    private static readonly ConcurrentDictionary<string, IReadOnlyList<CountryDialInfo>> Cache = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public IReadOnlyList<CountryDialInfo> GetCountries()
    {
        var culture = CultureInfo.CurrentUICulture.Name;
        return Cache.GetOrAdd(culture, static _ => Build());
    }

    private static IReadOnlyList<CountryDialInfo> Build()
    {
        var list = new List<CountryDialInfo>();
        foreach (var region in Util.GetSupportedRegions())
        {
            if (region.Length != 2 || !region.All(static c => c is >= 'A' and <= 'Z'))
            {
                continue;
            }

            string name;
            try
            {
                name = new RegionInfo(region).DisplayName;
            }
            catch (ArgumentException)
            {
                continue;
            }

            list.Add(new CountryDialInfo(region, name, Util.GetCountryCodeForRegion(region)));
        }

        list.Sort(static (a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCulture));
        return list;
    }
}
