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

    [Fact]
    public void ConcurrencyFailure_UsesLocalizedMessage()
    {
        var describer = CreateDescriber();

        var error = describer.ConcurrencyFailure();

        Assert.Equal("ConcurrencyFailure", error.Code);
        Assert.Equal("Optimistic concurrency failure, object has been modified.", error.Description);
    }

    [Fact]
    public void PasswordMismatch_UsesLocalizedMessage()
    {
        var describer = CreateDescriber();

        var error = describer.PasswordMismatch();

        Assert.Equal("PasswordMismatch", error.Code);
        Assert.Equal("Incorrect password.", error.Description);
    }

    [Fact]
    public void InvalidToken_UsesLocalizedMessage()
    {
        var describer = CreateDescriber();

        var error = describer.InvalidToken();

        Assert.Equal("InvalidToken", error.Code);
        Assert.Equal("Invalid token.", error.Description);
    }

    [Fact]
    public void RecoveryCodeRedemptionFailed_UsesLocalizedMessage()
    {
        var describer = CreateDescriber();

        var error = describer.RecoveryCodeRedemptionFailed();

        Assert.Equal("RecoveryCodeRedemptionFailed", error.Code);
        Assert.Equal("Recovery code redemption failed.", error.Description);
    }

    [Fact]
    public void LoginAlreadyAssociated_UsesLocalizedMessage()
    {
        var describer = CreateDescriber();

        var error = describer.LoginAlreadyAssociated();

        Assert.Equal("LoginAlreadyAssociated", error.Code);
        Assert.Equal("A user with this login already exists.", error.Description);
    }

    [Fact]
    public void InvalidUserName_UsesLocalizedMessageWithUserName()
    {
        var describer = CreateDescriber();

        var error = describer.InvalidUserName("alice!");

        Assert.Equal("InvalidUserName", error.Code);
        Assert.Equal("User name 'alice!' is invalid, can only contain letters or digits.", error.Description);
    }

    [Fact]
    public void InvalidEmail_UsesLocalizedMessageWithEmail()
    {
        var describer = CreateDescriber();

        var error = describer.InvalidEmail("not-an-email");

        Assert.Equal("InvalidEmail", error.Code);
        Assert.Equal("Email 'not-an-email' is invalid.", error.Description);
    }

    [Fact]
    public void DuplicateEmail_UsesLocalizedMessageWithEmail()
    {
        var describer = CreateDescriber();

        var error = describer.DuplicateEmail("alice@example.com");

        Assert.Equal("DuplicateEmail", error.Code);
        Assert.Equal("Email 'alice@example.com' is already taken.", error.Description);
    }

    [Fact]
    public void InvalidRoleName_UsesLocalizedMessageWithRoleName()
    {
        var describer = CreateDescriber();

        var error = describer.InvalidRoleName("admin!");

        Assert.Equal("InvalidRoleName", error.Code);
        Assert.Equal("Role name 'admin!' is invalid.", error.Description);
    }

    [Fact]
    public void DuplicateRoleName_UsesLocalizedMessageWithRoleName()
    {
        var describer = CreateDescriber();

        var error = describer.DuplicateRoleName("admin");

        Assert.Equal("DuplicateRoleName", error.Code);
        Assert.Equal("Role name 'admin' is already taken.", error.Description);
    }

    [Fact]
    public void UserAlreadyHasPassword_UsesLocalizedMessage()
    {
        var describer = CreateDescriber();

        var error = describer.UserAlreadyHasPassword();

        Assert.Equal("UserAlreadyHasPassword", error.Code);
        Assert.Equal("User already has a password set.", error.Description);
    }

    [Fact]
    public void UserLockoutNotEnabled_UsesLocalizedMessage()
    {
        var describer = CreateDescriber();

        var error = describer.UserLockoutNotEnabled();

        Assert.Equal("UserLockoutNotEnabled", error.Code);
        Assert.Equal("Lockout is not enabled for this user.", error.Description);
    }

    [Fact]
    public void UserAlreadyInRole_UsesLocalizedMessageWithRoleName()
    {
        var describer = CreateDescriber();

        var error = describer.UserAlreadyInRole("admin");

        Assert.Equal("UserAlreadyInRole", error.Code);
        Assert.Equal("User already in role 'admin'.", error.Description);
    }

    [Fact]
    public void UserNotInRole_UsesLocalizedMessageWithRoleName()
    {
        var describer = CreateDescriber();

        var error = describer.UserNotInRole("admin");

        Assert.Equal("UserNotInRole", error.Code);
        Assert.Equal("User is not in role 'admin'.", error.Description);
    }

    [Fact]
    public void PasswordRequiresUniqueChars_UsesLocalizedMessageWithCount()
    {
        var describer = CreateDescriber();

        var error = describer.PasswordRequiresUniqueChars(4);

        Assert.Equal("PasswordRequiresUniqueChars", error.Code);
        Assert.Equal("Passwords must use at least 4 different characters.", error.Description);
    }

    [Fact]
    public void PasswordRequiresNonAlphanumeric_UsesLocalizedMessage()
    {
        var describer = CreateDescriber();

        var error = describer.PasswordRequiresNonAlphanumeric();

        Assert.Equal("PasswordRequiresNonAlphanumeric", error.Code);
        Assert.Equal("Passwords must have at least one non alphanumeric character.", error.Description);
    }

    [Fact]
    public void PasswordRequiresDigit_UsesLocalizedMessage()
    {
        var describer = CreateDescriber();

        var error = describer.PasswordRequiresDigit();

        Assert.Equal("PasswordRequiresDigit", error.Code);
        Assert.Equal("Passwords must have at least one digit ('0'-'9').", error.Description);
    }

    [Fact]
    public void PasswordRequiresLower_UsesLocalizedMessage()
    {
        var describer = CreateDescriber();

        var error = describer.PasswordRequiresLower();

        Assert.Equal("PasswordRequiresLower", error.Code);
        Assert.Equal("Passwords must have at least one lowercase ('a'-'z').", error.Description);
    }

    [Fact]
    public void PasswordRequiresUpper_UsesLocalizedMessage()
    {
        var describer = CreateDescriber();

        var error = describer.PasswordRequiresUpper();

        Assert.Equal("PasswordRequiresUpper", error.Code);
        Assert.Equal("Passwords must have at least one uppercase ('A'-'Z').", error.Description);
    }
}
