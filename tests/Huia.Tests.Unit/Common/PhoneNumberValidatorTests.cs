using Huia.Common;

namespace Huia.Tests.Unit.Common;

public class PhoneNumberValidatorTests
{
    [Theory]
    [InlineData("+14155552671", "+14155552671")]
    [InlineData("+1 415 555 2671", "+14155552671")]
    [InlineData("+1 (415) 555-2671", "+14155552671")]
    [InlineData("+442071838750", "+442071838750")]
    public void TryNormalize_WithValidNumber_ReturnsE164(string input, string expected) =>
        Assert.Equal(expected, PhoneNumberValidator.TryNormalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a phone number")]
    [InlineData("4155552671")] // no "+" prefix / country code — deliberately not guessed at, see doc comment
    [InlineData("+1")]
    [InlineData("+9999999999999999")]
    [InlineData("+15551234567")] // fictitious NANP number (the "555-1234" movie-phone pattern) — well-formed but not a real, assignable number
    public void TryNormalize_WithInvalidNumber_ReturnsNull(string? input) =>
        Assert.Null(PhoneNumberValidator.TryNormalize(input));
}
