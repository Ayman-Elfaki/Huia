using Huia.Common;

namespace Huia.Tests.Unit.Common;

public class PhoneNumberMaskerTests
{
    [Fact]
    public void Mask_WithUsNumber_KeepsCountryCodeAndLast4()
    {
        Assert.Equal("+1***-***-4567", PhoneNumberMasker.Mask("+15551234567"));
    }

    [Fact]
    public void Mask_WithUkNumber_KeepsCountryCodeAndLast4()
    {
        Assert.Equal("+44***-***-8750", PhoneNumberMasker.Mask("+442071838750"));
    }

    [Fact]
    public void Mask_NeverContainsTheRawMiddleDigits()
    {
        const string raw = "+15551234567";
        var masked = PhoneNumberMasker.Mask(raw);

        Assert.DoesNotContain("555123", masked, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Mask_WithEmptyOrNullInput_ReturnsInputUnchanged(string? input)
    {
        Assert.Equal(input, PhoneNumberMasker.Mask(input!));
    }

    [Fact]
    public void Mask_WithUnparsableInput_StillMasksRatherThanThrowing()
    {
        var masked = PhoneNumberMasker.Mask("not-a-phone-number-1234");

        Assert.EndsWith("1234", masked, StringComparison.Ordinal);
        Assert.DoesNotContain("not-a-phone-number", masked, StringComparison.Ordinal);
    }
}
