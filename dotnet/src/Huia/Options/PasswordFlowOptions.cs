namespace Huia.Options;

/// <summary>Settings for the interactive username/password sign-in flow.</summary>
public sealed class PasswordFlowOptions : IHuiaOptionsSection
{
    /// <summary>
    /// Whether the interactive username/password form is available for this tenant. Off unless
    /// <see cref="HuiaTenantAuthenticationOptions.UsePasswordFlow"/> was called (or this is set directly),
    /// mirroring the opt-in shape of the passwordless umbrella.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Whether a confirmed email address is required before an interactive password sign-in is allowed.
    /// On by default. Does not apply to the passwordless SMS or external-login flows.
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

    void IHuiaOptionsSection.Validate(string path, List<string> errors)
    {
        errors.Require(MinimumLength is >= 6 and <= 256, HuiaOptionsValidation.Combine(path, nameof(MinimumLength)),
            "must be between 6 and 256.");
        errors.Require(RequiredUniqueChars is >= 1 and <= 256, HuiaOptionsValidation.Combine(path, nameof(RequiredUniqueChars)),
            "must be between 1 and 256.");
    }
}
