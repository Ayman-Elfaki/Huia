using Huia.Localization;

namespace Huia.Tests.Unit.Localization;

public class LocalizationBuilderTests
{
    [Fact]
    public void Cultures_DefaultsToEnglishAndArabic()
    {
        var builder = new LocalizationBuilder();

        Assert.Equal(["en", "ar"], builder.Cultures.Select(c => c.Name));
        Assert.Equal("en", builder.DefaultCulture);
    }

    [Fact]
    public void AddCulture_AppendsNewCulture()
    {
        var builder = new LocalizationBuilder();

        builder.AddCulture("fr");

        Assert.Contains(builder.Cultures, c => c.Name == "fr");
    }

    [Fact]
    public void AddCulture_IsIdempotent_ForAlreadyPresentCulture()
    {
        var builder = new LocalizationBuilder();

        builder.AddCulture("ar");

        Assert.Equal(2, builder.Cultures.Count);
    }

    [Fact]
    public void SetDefaultCulture_OverridesDefault()
    {
        var builder = new LocalizationBuilder();
        builder.AddCulture("fr");

        builder.SetDefaultCulture("fr");

        Assert.Equal("fr", builder.DefaultCulture);
    }
}