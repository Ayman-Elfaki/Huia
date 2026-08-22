using Huia.Identity;

namespace Huia.Tests.Unit.Identity;

/// <summary>
/// Verifies <see cref="HuiaErrorDescriber"/>'s hardcoded English defaults, which apply to any consumer that
/// registers it directly (bypassing <c>Huia.Emails.LocalizedHuiaErrorDescriber</c>'s resource-based messages).
/// </summary>
public class HuiaErrorDescriberTests
{
    private static readonly HuiaErrorDescriber Describer = new();

    [Fact]
    public void DefaultError_UsesHardcodedEnglishMessage()
    {
        var error = Describer.DefaultError();

        Assert.Equal("DefaultError", error.Code);
        Assert.Equal("An unknown failure has occurred.", error.Description);
    }

    [Fact]
    public void ConcurrencyFailure_UsesHardcodedEnglishMessage()
    {
        var error = Describer.ConcurrencyFailure();

        Assert.Equal("ConcurrencyFailure", error.Code);
        Assert.Equal("Optimistic concurrency failure, object has been modified.", error.Description);
    }

    [Fact]
    public void PasswordMismatch_UsesHardcodedEnglishMessage()
    {
        var error = Describer.PasswordMismatch();

        Assert.Equal("PasswordMismatch", error.Code);
        Assert.Equal("Incorrect password.", error.Description);
    }

    [Fact]
    public void InvalidToken_UsesHardcodedEnglishMessage()
    {
        var error = Describer.InvalidToken();

        Assert.Equal("InvalidToken", error.Code);
        Assert.Equal("Invalid token.", error.Description);
    }

    [Fact]
    public void RecoveryCodeRedemptionFailed_UsesHardcodedEnglishMessage()
    {
        var error = Describer.RecoveryCodeRedemptionFailed();

        Assert.Equal("RecoveryCodeRedemptionFailed", error.Code);
        Assert.Equal("Recovery code redemption failed.", error.Description);
    }

    [Fact]
    public void LoginAlreadyAssociated_UsesHardcodedEnglishMessage()
    {
        var error = Describer.LoginAlreadyAssociated();

        Assert.Equal("LoginAlreadyAssociated", error.Code);
        Assert.Equal("A user with this login already exists.", error.Description);
    }

    [Fact]
    public void InvalidUserName_UsesHardcodedEnglishMessageWithUserName()
    {
        var error = Describer.InvalidUserName("alice!");

        Assert.Equal("InvalidUserName", error.Code);
        Assert.Equal("User name 'alice!' is invalid, can only contain letters or digits.", error.Description);
    }

    [Fact]
    public void InvalidEmail_UsesHardcodedEnglishMessageWithEmail()
    {
        var error = Describer.InvalidEmail("not-an-email");

        Assert.Equal("InvalidEmail", error.Code);
        Assert.Equal("Email 'not-an-email' is invalid.", error.Description);
    }

    [Fact]
    public void DuplicateUserName_UsesHardcodedEnglishMessageWithUserName()
    {
        var error = Describer.DuplicateUserName("alice");

        Assert.Equal("DuplicateUserName", error.Code);
        Assert.Equal("User name 'alice' is already taken.", error.Description);
    }

    [Fact]
    public void DuplicateEmail_UsesHardcodedEnglishMessageWithEmail()
    {
        var error = Describer.DuplicateEmail("alice@example.com");

        Assert.Equal("DuplicateEmail", error.Code);
        Assert.Equal("Email 'alice@example.com' is already taken.", error.Description);
    }

    [Fact]
    public void InvalidRoleName_UsesHardcodedEnglishMessageWithRoleName()
    {
        var error = Describer.InvalidRoleName("admin!");

        Assert.Equal("InvalidRoleName", error.Code);
        Assert.Equal("Role name 'admin!' is invalid.", error.Description);
    }

    [Fact]
    public void DuplicateRoleName_UsesHardcodedEnglishMessageWithRoleName()
    {
        var error = Describer.DuplicateRoleName("admin");

        Assert.Equal("DuplicateRoleName", error.Code);
        Assert.Equal("Role name 'admin' is already taken.", error.Description);
    }

    [Fact]
    public void UserAlreadyHasPassword_UsesHardcodedEnglishMessage()
    {
        var error = Describer.UserAlreadyHasPassword();

        Assert.Equal("UserAlreadyHasPassword", error.Code);
        Assert.Equal("User already has a password set.", error.Description);
    }

    [Fact]
    public void UserLockoutNotEnabled_UsesHardcodedEnglishMessage()
    {
        var error = Describer.UserLockoutNotEnabled();

        Assert.Equal("UserLockoutNotEnabled", error.Code);
        Assert.Equal("Lockout is not enabled for this user.", error.Description);
    }

    [Fact]
    public void UserAlreadyInRole_UsesHardcodedEnglishMessageWithRoleName()
    {
        var error = Describer.UserAlreadyInRole("admin");

        Assert.Equal("UserAlreadyInRole", error.Code);
        Assert.Equal("User already in role 'admin'.", error.Description);
    }

    [Fact]
    public void UserNotInRole_UsesHardcodedEnglishMessageWithRoleName()
    {
        var error = Describer.UserNotInRole("admin");

        Assert.Equal("UserNotInRole", error.Code);
        Assert.Equal("User is not in role 'admin'.", error.Description);
    }

    [Fact]
    public void PasswordTooShort_UsesHardcodedEnglishMessageWithLength()
    {
        var error = Describer.PasswordTooShort(8);

        Assert.Equal("PasswordTooShort", error.Code);
        Assert.Equal("Passwords must be at least 8 characters.", error.Description);
    }

    [Fact]
    public void PasswordRequiresUniqueChars_UsesHardcodedEnglishMessageWithCount()
    {
        var error = Describer.PasswordRequiresUniqueChars(4);

        Assert.Equal("PasswordRequiresUniqueChars", error.Code);
        Assert.Equal("Passwords must use at least 4 different characters.", error.Description);
    }

    [Fact]
    public void PasswordRequiresNonAlphanumeric_UsesHardcodedEnglishMessage()
    {
        var error = Describer.PasswordRequiresNonAlphanumeric();

        Assert.Equal("PasswordRequiresNonAlphanumeric", error.Code);
        Assert.Equal("Passwords must have at least one non alphanumeric character.", error.Description);
    }

    [Fact]
    public void PasswordRequiresDigit_UsesHardcodedEnglishMessage()
    {
        var error = Describer.PasswordRequiresDigit();

        Assert.Equal("PasswordRequiresDigit", error.Code);
        Assert.Equal("Passwords must have at least one digit ('0'-'9').", error.Description);
    }

    [Fact]
    public void PasswordRequiresLower_UsesHardcodedEnglishMessage()
    {
        var error = Describer.PasswordRequiresLower();

        Assert.Equal("PasswordRequiresLower", error.Code);
        Assert.Equal("Passwords must have at least one lowercase ('a'-'z').", error.Description);
    }

    [Fact]
    public void PasswordRequiresUpper_UsesHardcodedEnglishMessage()
    {
        var error = Describer.PasswordRequiresUpper();

        Assert.Equal("PasswordRequiresUpper", error.Code);
        Assert.Equal("Passwords must have at least one uppercase ('A'-'Z').", error.Description);
    }
}
