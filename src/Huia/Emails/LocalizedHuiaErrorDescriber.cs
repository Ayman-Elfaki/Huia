using Huia.Identity;
using Huia.Localization;
using Microsoft.Extensions.Localization;

namespace Huia.Emails;

/// <summary>
/// Localizes <see cref="HuiaUserManager"/>/<see cref="HuiaRoleManager"/>'s validation messages (e.g.
/// "Passwords must have at least one digit") into <see cref="HuiaResources"/>, resolving them through the
/// same <see cref="IStringLocalizer"/> Huia.UI's pages and <see cref="HuiaEmailTemplate"/> use. Registered by
/// default via <c>services.AddHuia(...)</c> as the <see cref="HuiaErrorDescriber"/> implementation; register
/// your own subclass after <c>AddHuia</c> to override it, or register a plain <see cref="HuiaErrorDescriber"/>
/// (or subclass) instead to skip localization entirely.
/// </summary>
public class LocalizedHuiaErrorDescriber(IStringLocalizer<HuiaResources> localizer) : HuiaErrorDescriber
{
    /// <inheritdoc />
    public override HuiaIdentityError DefaultError() => Describe(nameof(DefaultError));

    /// <inheritdoc />
    public override HuiaIdentityError ConcurrencyFailure() => Describe(nameof(ConcurrencyFailure));

    /// <inheritdoc />
    public override HuiaIdentityError PasswordMismatch() => Describe(nameof(PasswordMismatch));

    /// <inheritdoc />
    public override HuiaIdentityError InvalidToken() => Describe(nameof(InvalidToken));

    /// <inheritdoc />
    public override HuiaIdentityError RecoveryCodeRedemptionFailed() => Describe(nameof(RecoveryCodeRedemptionFailed));

    /// <inheritdoc />
    public override HuiaIdentityError LoginAlreadyAssociated() => Describe(nameof(LoginAlreadyAssociated));

    /// <inheritdoc />
    public override HuiaIdentityError InvalidUserName(string? userName) => Describe(nameof(InvalidUserName), userName);

    /// <inheritdoc />
    public override HuiaIdentityError InvalidEmail(string? email) => Describe(nameof(InvalidEmail), email);

    /// <inheritdoc />
    public override HuiaIdentityError DuplicateUserName(string userName) => Describe(nameof(DuplicateUserName), userName);

    /// <inheritdoc />
    public override HuiaIdentityError DuplicateEmail(string email) => Describe(nameof(DuplicateEmail), email);

    /// <inheritdoc />
    public override HuiaIdentityError InvalidRoleName(string? role) => Describe(nameof(InvalidRoleName), role);

    /// <inheritdoc />
    public override HuiaIdentityError DuplicateRoleName(string role) => Describe(nameof(DuplicateRoleName), role);

    /// <inheritdoc />
    public override HuiaIdentityError UserAlreadyHasPassword() => Describe(nameof(UserAlreadyHasPassword));

    /// <inheritdoc />
    public override HuiaIdentityError UserLockoutNotEnabled() => Describe(nameof(UserLockoutNotEnabled));

    /// <inheritdoc />
    public override HuiaIdentityError UserAlreadyInRole(string role) => Describe(nameof(UserAlreadyInRole), role);

    /// <inheritdoc />
    public override HuiaIdentityError UserNotInRole(string role) => Describe(nameof(UserNotInRole), role);

    /// <inheritdoc />
    public override HuiaIdentityError PasswordTooShort(int length) => Describe(nameof(PasswordTooShort), length);

    /// <inheritdoc />
    public override HuiaIdentityError PasswordRequiresUniqueChars(int uniqueChars) =>
        Describe(nameof(PasswordRequiresUniqueChars), uniqueChars);

    /// <inheritdoc />
    public override HuiaIdentityError PasswordRequiresNonAlphanumeric() => Describe(nameof(PasswordRequiresNonAlphanumeric));

    /// <inheritdoc />
    public override HuiaIdentityError PasswordRequiresDigit() => Describe(nameof(PasswordRequiresDigit));

    /// <inheritdoc />
    public override HuiaIdentityError PasswordRequiresLower() => Describe(nameof(PasswordRequiresLower));

    /// <inheritdoc />
    public override HuiaIdentityError PasswordRequiresUpper() => Describe(nameof(PasswordRequiresUpper));

    private HuiaIdentityError Describe(string code, params object?[] args) => new()
    {
        Code = code,
        Description = localizer[$"IdentityError{code}", args.Select(arg => arg ?? string.Empty).ToArray()]
    };
}
