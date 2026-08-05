using Huia.Emails;
using Huia.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Huia.Tests.Unit.Identity;

public class IdentityErrorDescriberTests
{
    private static IdentityErrorDescriber CreateDescriber()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization();
        var provider = services.BuildServiceProvider();

        return new IdentityErrorDescriber(provider.GetRequiredService<IStringLocalizer<HuiaResources>>());
    }

    [Fact]
    public void PasswordTooShort_UsesLocalizedMessageWithLength()
    {
        var describer = CreateDescriber();

        var error = describer.PasswordTooShort(8);

        Assert.Equal("PasswordTooShort", error.Code);
        Assert.Equal("Passwords must be at least 8 characters.", error.Description);
    }

    [Fact]
    public void DuplicateUserName_UsesLocalizedMessageWithUserName()
    {
        var describer = CreateDescriber();

        var error = describer.DuplicateUserName("alice");

        Assert.Equal("DuplicateUserName", error.Code);
        Assert.Equal("User name 'alice' is already taken.", error.Description);
    }

    [Fact]
    public void DefaultError_UsesLocalizedMessage()
    {
        var describer = CreateDescriber();

        var error = describer.DefaultError();

        Assert.Equal("DefaultError", error.Code);
        Assert.Equal("An unknown error occurred.", error.Description);
    }
}
