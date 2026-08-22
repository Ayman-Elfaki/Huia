namespace Huia.Identity;

/// <summary>
/// Produces the <see cref="HuiaIdentityError"/>s returned by <see cref="HuiaUserManager"/>/<see cref="HuiaRoleManager"/>
/// (e.g. "Passwords must have at least one digit"). Every method returns a hardcoded English default, matching
/// ASP.NET Core Identity's own <c>IdentityErrorDescriber</c> wording. Registered by default via
/// <c>services.AddHuia(...)</c>, which registers <c>LocalizedHuiaErrorDescriber</c> (in the main Huia package) as
/// the implementation to give consumers localized English/Arabic messages out of the box; register your own
/// <see cref="HuiaErrorDescriber"/> after <c>AddHuia</c> to override it. Replaces ASP.NET Core Identity's
/// <c>IdentityErrorDescriber</c>.
/// </summary>
public class HuiaErrorDescriber
{
    /// <summary>An unattributed failure.</summary>
    public virtual HuiaIdentityError DefaultError() => new()
    {
        Code = nameof(DefaultError),
        Description = "An unknown failure has occurred."
    };

    /// <summary>The entity was modified concurrently.</summary>
    public virtual HuiaIdentityError ConcurrencyFailure() => new()
    {
        Code = nameof(ConcurrencyFailure),
        Description = "Optimistic concurrency failure, object has been modified."
    };

    /// <summary>The supplied password didn't match.</summary>
    public virtual HuiaIdentityError PasswordMismatch() => new()
    {
        Code = nameof(PasswordMismatch),
        Description = "Incorrect password."
    };

    /// <summary>The supplied token was invalid or expired.</summary>
    public virtual HuiaIdentityError InvalidToken() => new()
    {
        Code = nameof(InvalidToken),
        Description = "Invalid token."
    };

    /// <summary>The supplied 2FA recovery code didn't match an unused one.</summary>
    public virtual HuiaIdentityError RecoveryCodeRedemptionFailed() => new()
    {
        Code = nameof(RecoveryCodeRedemptionFailed),
        Description = "Recovery code redemption failed."
    };

    /// <summary>The external login is already linked to a (possibly different) account.</summary>
    public virtual HuiaIdentityError LoginAlreadyAssociated() => new()
    {
        Code = nameof(LoginAlreadyAssociated),
        Description = "A user with this login already exists."
    };

    /// <summary>The username is invalid.</summary>
    public virtual HuiaIdentityError InvalidUserName(string? userName) => new()
    {
        Code = nameof(InvalidUserName),
        Description = $"User name '{userName}' is invalid, can only contain letters or digits."
    };

    /// <summary>The email address is invalid.</summary>
    public virtual HuiaIdentityError InvalidEmail(string? email) => new()
    {
        Code = nameof(InvalidEmail),
        Description = $"Email '{email}' is invalid."
    };

    /// <summary>The username is already taken.</summary>
    public virtual HuiaIdentityError DuplicateUserName(string userName) => new()
    {
        Code = nameof(DuplicateUserName),
        Description = $"User name '{userName}' is already taken."
    };

    /// <summary>The email address is already taken.</summary>
    public virtual HuiaIdentityError DuplicateEmail(string email) => new()
    {
        Code = nameof(DuplicateEmail),
        Description = $"Email '{email}' is already taken."
    };

    /// <summary>The role name is invalid.</summary>
    public virtual HuiaIdentityError InvalidRoleName(string? role) => new()
    {
        Code = nameof(InvalidRoleName),
        Description = $"Role name '{role}' is invalid."
    };

    /// <summary>The role name is already taken.</summary>
    public virtual HuiaIdentityError DuplicateRoleName(string role) => new()
    {
        Code = nameof(DuplicateRoleName),
        Description = $"Role name '{role}' is already taken."
    };

    /// <summary>The user already has a password set.</summary>
    public virtual HuiaIdentityError UserAlreadyHasPassword() => new()
    {
        Code = nameof(UserAlreadyHasPassword),
        Description = "User already has a password set."
    };

    /// <summary>Lockout isn't enabled for this user.</summary>
    public virtual HuiaIdentityError UserLockoutNotEnabled() => new()
    {
        Code = nameof(UserLockoutNotEnabled),
        Description = "Lockout is not enabled for this user."
    };

    /// <summary>The user is already in the named role.</summary>
    public virtual HuiaIdentityError UserAlreadyInRole(string role) => new()
    {
        Code = nameof(UserAlreadyInRole),
        Description = $"User already in role '{role}'."
    };

    /// <summary>The user isn't in the named role.</summary>
    public virtual HuiaIdentityError UserNotInRole(string role) => new()
    {
        Code = nameof(UserNotInRole),
        Description = $"User is not in role '{role}'."
    };

    /// <summary>The password is shorter than the configured minimum length.</summary>
    public virtual HuiaIdentityError PasswordTooShort(int length) => new()
    {
        Code = nameof(PasswordTooShort),
        Description = $"Passwords must be at least {length} characters."
    };

    /// <summary>The password doesn't have enough distinct characters.</summary>
    public virtual HuiaIdentityError PasswordRequiresUniqueChars(int uniqueChars) => new()
    {
        Code = nameof(PasswordRequiresUniqueChars),
        Description = $"Passwords must use at least {uniqueChars} different characters."
    };

    /// <summary>The password needs a non-alphanumeric character.</summary>
    public virtual HuiaIdentityError PasswordRequiresNonAlphanumeric() => new()
    {
        Code = nameof(PasswordRequiresNonAlphanumeric),
        Description = "Passwords must have at least one non alphanumeric character."
    };

    /// <summary>The password needs a digit.</summary>
    public virtual HuiaIdentityError PasswordRequiresDigit() => new()
    {
        Code = nameof(PasswordRequiresDigit),
        Description = "Passwords must have at least one digit ('0'-'9')."
    };

    /// <summary>The password needs a lowercase letter.</summary>
    public virtual HuiaIdentityError PasswordRequiresLower() => new()
    {
        Code = nameof(PasswordRequiresLower),
        Description = "Passwords must have at least one lowercase ('a'-'z')."
    };

    /// <summary>The password needs an uppercase letter.</summary>
    public virtual HuiaIdentityError PasswordRequiresUpper() => new()
    {
        Code = nameof(PasswordRequiresUpper),
        Description = "Passwords must have at least one uppercase ('A'-'Z')."
    };
}
