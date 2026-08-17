using System.Globalization;
using Huia.Common;

namespace Huia.Tests.Unit.Common;

public class CountryPhoneCodeProviderTests
{
    [Fact]
    public void GetAll_ReturnsRegionsWithDialCodesAndNames_SortedByDisplayName()
    {
        var countries = CountryPhoneCodeProvider.GetAll();

        Assert.NotEmpty(countries);
        Assert.All(countries, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.RegionCode));
            Assert.StartsWith("+", c.DialCode, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(c.DisplayName));
        });

        // Same comparer CountryPhoneCodeProvider.GetAll itself sorts with (see its own doc comment) — a
        // culture-aware comparer can disagree with plain ordinal on where accented names land (e.g. "Åland"),
        // so re-sorting with anything else here would make this assert the wrong thing.
        var comparer = StringComparer.Create(CultureInfo.CurrentUICulture, ignoreCase: true);
        var sorted = countries.OrderBy(c => c.DisplayName, comparer).ToList();
        Assert.Equal(sorted.Select(c => c.RegionCode), countries.Select(c => c.RegionCode), StringComparer.Ordinal);
    }

    [Fact]
    public void GetAll_IncludesKnownRegionWithCorrectDialCode()
    {
        var countries = CountryPhoneCodeProvider.GetAll();

        var us = Assert.Single(countries, c => string.Equals(c.RegionCode, "US", StringComparison.Ordinal));
        Assert.Equal("+1", us.DialCode);
    }

    [Theory]
    [InlineData("US")]
    [InlineData("gb")]
    [InlineData("Fr")]
    public void IsSupportedRegion_WithRealRegion_ReturnsTrueRegardlessOfCase(string regionCode) =>
        Assert.True(CountryPhoneCodeProvider.IsSupportedRegion(regionCode));

    [Theory]
    [InlineData("ZZ")]
    [InlineData("XX")]
    [InlineData("")]
    public void IsSupportedRegion_WithUnknownRegion_ReturnsFalse(string regionCode) =>
        Assert.False(CountryPhoneCodeProvider.IsSupportedRegion(regionCode));
}
