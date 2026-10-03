namespace Huia.Options;

/// <summary>
/// Interactive email and password authentication method options.
/// </summary>
public class EmailPasswordAuthenticationMethod : HuiaAuthenticationMethod
{
    /// <inheritdoc />
    public override string MethodType => "EmailPassword";

    /// <summary>Minimum password length. Default is 8.</summary>
    public int MinimumLength { get; set; } = 8;

    /// <summary>Whether a digit is required.</summary>
    public bool RequireDigit { get; set; } = true;

    /// <summary>Whether an uppercase letter is required.</summary>
    public bool RequireUppercase { get; set; } = true;

    /// <summary>Whether a lowercase letter is required.</summary>
    public bool RequireLowercase { get; set; } = true;

    /// <summary>Whether a non-alphanumeric character is required.</summary>
    public bool RequireNonAlphanumeric { get; set; } = false;

    /// <summary>Number of distinct characters required.</summary>
    public int RequiredUniqueChars { get; set; } = 1;

    /// <summary>Whether users must confirm their email before signing in.</summary>
    public bool RequireConfirmedEmail { get; set; } = false;

    /// <summary>Whether email must be unique across accounts in this tenant.</summary>
    public bool RequireUniqueEmail { get; set; } = false;

    /// <summary>Whether public self-service account registration is permitted.</summary>
    public bool AllowSelfServiceRegistration { get; set; } = true;

    /// <summary>Maximum failed attempts before account is locked out.</summary>
    public int MaxFailedAccessAttempts { get; set; } = 5;

    /// <summary>Duration of lockout after maximum failed attempts.</summary>
    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(15);

    /// <inheritdoc />
    public override void Validate(string path, List<string> errors)
    {
        base.Validate(path, errors);

        errors.Require(MinimumLength >= 6, HuiaOptionsValidation.Combine(path, nameof(MinimumLength)),
            "must be at least 6 characters.");
        errors.Require(MaxFailedAccessAttempts >= 1, HuiaOptionsValidation.Combine(path, nameof(MaxFailedAccessAttempts)),
            "must be at least 1.");
        errors.Require(LockoutDuration > TimeSpan.Zero, HuiaOptionsValidation.Combine(path, nameof(LockoutDuration)),
            "must be greater than zero.");
    }
}
