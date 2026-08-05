using Huia.Localization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Localization;

namespace Huia.Emails;


/// <summary>
/// Localizes ASP.NET Core Identity's validation messages (e.g. "Passwords must have at least one digit")
/// into <see cref="HuiaResources"/>, resolving them through the same <see cref="IStringLocalizer"/> Huia.UI's
/// pages and <see cref="HuiaEmailTemplate"/> use. Registered by default via <c>services.AddHuia(...)</c>
/// (<c>.AddErrorDescriber&lt;HuiaIdentityErrorDescriber&gt;()</c>); register your own
/// <see cref="Microsoft.AspNetCore.Identity.IdentityErrorDescriber"/> after <c>AddHuia</c> to override it.
/// </summary>
public sealed class IdentityErrorDescriber(IStringLocalizer<HuiaResources> localizer) : Microsoft.AspNetCore.Identity.IdentityErrorDescriber
{
    /// <inheritdoc />
    public override IdentityError DefaultError() => Describe(nameof(DefaultError));

    /// <inheritdoc />
    public override IdentityError ConcurrencyFailure() => Describe(nameof(ConcurrencyFailure));

    /// <inheritdoc />
    public override IdentityError PasswordMismatch() => Describe(nameof(PasswordMismatch));

    /// <inheritdoc />
    public override IdentityError InvalidToken() => Describe(nameof(InvalidToken));

    /// <inheritdoc />
    public override IdentityError RecoveryCodeRedemptionFailed() => Describe(nameof(RecoveryCodeRedemptionFailed));

    /// <inheritdoc />
    public override IdentityError LoginAlreadyAssociated() => Describe(nameof(LoginAlreadyAssociated));

    /// <inheritdoc />
    public override IdentityError InvalidUserName(string? userName) => Describe(nameof(InvalidUserName), userName);

    /// <inheritdoc />
    public override IdentityError InvalidEmail(string? email) => Describe(nameof(InvalidEmail), email);

    /// <inheritdoc />
    public override IdentityError DuplicateUserName(string userName) => Describe(nameof(DuplicateUserName), userName);

    /// <inheritdoc />
    public override IdentityError DuplicateEmail(string email) => Describe(nameof(DuplicateEmail), email);

    /// <inheritdoc />
    public override IdentityError InvalidRoleName(string? role) => Describe(nameof(InvalidRoleName), role);

    /// <inheritdoc />
    public override IdentityError DuplicateRoleName(string role) => Describe(nameof(DuplicateRoleName), role);

    /// <inheritdoc />
    public override IdentityError UserAlreadyHasPassword() => Describe(nameof(UserAlreadyHasPassword));

    /// <inheritdoc />
    public override IdentityError UserLockoutNotEnabled() => Describe(nameof(UserLockoutNotEnabled));

    /// <inheritdoc />
    public override IdentityError UserAlreadyInRole(string role) => Describe(nameof(UserAlreadyInRole), role);

    /// <inheritdoc />
    public override IdentityError UserNotInRole(string role) => Describe(nameof(UserNotInRole), role);

    /// <inheritdoc />
    public override IdentityError PasswordTooShort(int length) => Describe(nameof(PasswordTooShort), length);

    /// <inheritdoc />
    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) =>
        Describe(nameof(PasswordRequiresUniqueChars), uniqueChars);

    /// <inheritdoc />
    public override IdentityError PasswordRequiresNonAlphanumeric() => Describe(nameof(PasswordRequiresNonAlphanumeric));

    /// <inheritdoc />
    public override IdentityError PasswordRequiresDigit() => Describe(nameof(PasswordRequiresDigit));

    /// <inheritdoc />
    public override IdentityError PasswordRequiresLower() => Describe(nameof(PasswordRequiresLower));

    /// <inheritdoc />
    public override IdentityError PasswordRequiresUpper() => Describe(nameof(PasswordRequiresUpper));

    private IdentityError Describe(string code, params object?[] args) => new()
    {
        Code = code,
        Description = localizer[$"IdentityError{code}", args.Select(arg => arg ?? string.Empty).ToArray()]
    };
}
