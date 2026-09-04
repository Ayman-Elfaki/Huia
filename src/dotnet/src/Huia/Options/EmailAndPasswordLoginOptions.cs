namespace Huia.Options;

/// <summary>Settings for the interactive email/password sign-in flow, including its own lockout policy.</summary>
public sealed class EmailAndPasswordLoginOptions : IHuiaOptionsSection
{
    /// <summary>
    /// Whether the interactive email/password form is available for this tenant. Off unless
    /// <see cref="HuiaTenantAuthenticationOptions.UseEmailAndPasswordLogin"/> was called (or this is
    /// set directly).
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Whether a confirmed email address is required before an interactive sign-in is allowed. On by
    /// default. Does not apply to the phone or external-login flows.
    /// </summary>
    public bool RequireConfirmedEmail { get; set; } = true;

    /// <summary>
    /// Whether an email address must be unique within the tenant. Off by default — when on, ASP.NET Core
    /// Identity's own tenant-scoped uniqueness check is applied on top of the composite index in
    /// <c>HuiaDbContext</c>.
    /// </summary>
    public bool RequireUniqueEmail { get; set; }

    /// <summary>
    /// Whether anonymous visitors may create their own accounts via the account UI. On by default;
    /// call <see cref="TenantOptions.DisableRegistration"/> to turn it off for a tenant.
    /// </summary>
    public bool AllowSelfServiceRegistration { get; set; } = true;

    /// <summary>Minimum password length enforced in addition to the ASP.NET Core Identity defaults.</summary>
    public int MinimumLength { get; set; } = 10;

    /// <summary>Whether a password must contain a digit (0-9).</summary>
    public bool RequireDigit { get; set; } = true;

    /// <summary>Whether a password must contain a lowercase letter (a-z).</summary>
    public bool RequireLowercase { get; set; } = true;

    /// <summary>Whether a password must contain an uppercase letter (A-Z).</summary>
    public bool RequireUppercase { get; set; } = true;

    /// <summary>Whether a password must contain a non-alphanumeric character.</summary>
    public bool RequireNonAlphanumeric { get; set; }

    /// <summary>The minimum number of distinct characters a password must contain.</summary>
    public int RequiredUniqueChars { get; set; } = 1;

    /// <summary>The number of failed sign-in attempts that triggers a lockout, for this flow only.</summary>
    public int MaxFailedAccessAttempts { get; set; } = 5;

    /// <summary>How long an account stays locked out once <see cref="MaxFailedAccessAttempts"/> is reached.</summary>
    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Whether a newly created user is subject to this flow's lockout policy.</summary>
    public bool AllowedForNewUsers { get; set; } = true;

    void IHuiaOptionsSection.Validate(string path, List<string> errors)
    {
        errors.Require(MinimumLength is >= 6 and <= 256, HuiaOptionsValidation.Combine(path, nameof(MinimumLength)),
            "must be between 6 and 256.");
        errors.Require(RequiredUniqueChars is >= 1 and <= 256, HuiaOptionsValidation.Combine(path, nameof(RequiredUniqueChars)),
            "must be between 1 and 256.");
        errors.Require(MaxFailedAccessAttempts >= 1,
            HuiaOptionsValidation.Combine(path, nameof(MaxFailedAccessAttempts)), "must be at least 1.");
        errors.Require(LockoutDuration > TimeSpan.Zero,
            HuiaOptionsValidation.Combine(path, nameof(LockoutDuration)), "must be greater than zero.");
    }
}
