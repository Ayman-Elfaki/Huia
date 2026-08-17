using Huia.Common;

namespace Huia.Tests.Unit.Common;

public class PhoneNumberValidatorTests
{
    [Theory]
    [InlineData("US", "4155552671", "+14155552671")]
    [InlineData("US", "+14155552671", "+14155552671")]
    [InlineData("US", "+1 415 555 2671", "+14155552671")]
    [InlineData("US", "(415) 555-2671", "+14155552671")]
    [InlineData("GB", "020 7183 8750", "+442071838750")]
    [InlineData("GB", "+442071838750", "+442071838750")]
    public void TryNormalize_WithValidNumberForRegion_ReturnsE164(string region, string input, string expected) =>
        Assert.Equal(expected, PhoneNumberValidator.TryNormalize(input, region));

    [Theory]
    [InlineData("US", null)]
    [InlineData("US", "")]
    [InlineData("US", "   ")]
    [InlineData("US", "not a phone number")]
    [InlineData("US", "+9999999999999999")]
    [InlineData("US", "5551234567")] // fictitious NANP number (the "555-1234" movie-phone pattern) — well-formed but not a real, assignable number
    [InlineData(null, "4155552671")]
    [InlineData("", "4155552671")]
    [InlineData("not-a-region", "4155552671")]
    public void TryNormalize_WithInvalidNumberOrRegion_ReturnsNull(string? region, string? input) =>
        Assert.Null(PhoneNumberValidator.TryNormalize(input, region));

    [Fact]
    public void TryNormalize_WithNumberBelongingToDifferentRegion_ReturnsNull() =>
        // A well-formed, valid GB number, but the caller selected "US" — the selected country and the number
        // must agree, not just the number be valid for *some* region.
        Assert.Null(PhoneNumberValidator.TryNormalize("+442071838750", "US"));
}
