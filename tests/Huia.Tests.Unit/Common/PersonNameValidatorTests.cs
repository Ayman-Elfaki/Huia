using Huia.Common;

namespace Huia.Tests.Unit.Common;

public class PersonNameValidatorTests
{
    [Theory]
    [InlineData("Ada")]
    [InlineData("Anne-Marie")]
    [InlineData("O'Brien")]
    [InlineData("Mary Jane")]
    [InlineData("José")]
    [InlineData("María José")]
    public void IsValid_WithLettersSpacesHyphensOrApostrophes_ReturnsTrue(string name) =>
        Assert.True(PersonNameValidator.IsValid(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ada99")]
    [InlineData("Ada!")]
    [InlineData("Ada_Lovelace")]
    [InlineData("<script>")]
    [InlineData("😀")]
    public void IsValid_WithMissingOrDisallowedCharacters_ReturnsFalse(string? name) =>
        Assert.False(PersonNameValidator.IsValid(name));

    [Fact]
    public void IsValid_AtMaxLength_ReturnsTrue() =>
        Assert.True(PersonNameValidator.IsValid(new string('a', PersonNameValidator.MaxLength)));

    [Fact]
    public void IsValid_OverMaxLength_ReturnsFalse() =>
        Assert.False(PersonNameValidator.IsValid(new string('a', PersonNameValidator.MaxLength + 1)));

    [Fact]
    public void DescribeError_IncludesTheGivenFieldLabel() =>
        Assert.Contains("First name", PersonNameValidator.DescribeError("First name"), StringComparison.Ordinal);
}
