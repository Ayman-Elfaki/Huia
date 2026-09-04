namespace Huia.Options;

/// <summary>
/// Settings for the passwordless SMS one-time-code sign-in, including its own lockout policy. Enabled
/// via <see cref="HuiaTenantAuthenticationOptions.UsePhoneLogin"/>.
/// </summary>
public sealed class PhoneOptions : IHuiaOptionsSection
{
    /// <summary>
    /// Whether an unknown but well-formed number may start a sign-in. The account is <em>not</em> created
    /// at code-request time: a pending record holds the hashed code until the user completes their
    /// profile, so a blank-name user is never persisted.
    /// </summary>
    public bool AllowAutoProvisioning { get; set; }

    /// <summary>
    /// Default region (ISO 3166-1 alpha-2, for example <c>US</c>) used to interpret phone numbers entered
    /// without a country code. Format-checked here only; an unknown-but-well-formed code degrades
    /// gracefully at runtime.
    /// </summary>
    public string? DefaultCountry { get; set; }

    /// <summary>Number of digits in the one-time code.</summary>
    public int CodeLength { get; set; } = 6;

    /// <summary>How long an issued code remains valid.</summary>
    public TimeSpan CodeLifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Maximum verification attempts against a single issued code before it is invalidated.</summary>
    public int MaxVerificationAttempts { get; set; } = 5;

    /// <summary>How long a pending auto-provisioning record (number with no account yet) is retained.</summary>
    public TimeSpan PendingSignupLifetime { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>When to require a CAPTCHA challenge before issuing a code.</summary>
    public CaptchaMode Captcha { get; set; } = CaptchaMode.None;

    /// <summary>
    /// Minimum wait before "send a new code" is offered again. Drives the visible cooldown counter on
    /// the verification page.
    /// </summary>
    public TimeSpan ResendCooldown { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Successful phone sign-ins allowed per <see cref="SuccessfulLoginWindow"/> for one number.</summary>
    public int SuccessfulLoginsPerWindow { get; set; } = 1;

    /// <summary>The rolling window <see cref="SuccessfulLoginsPerWindow"/> applies over.</summary>
    public TimeSpan SuccessfulLoginWindow { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Absolute ceiling on successful phone sign-ins per number per rolling 24 hours.</summary>
    public int SuccessfulLoginsPerDay { get; set; } = 5;

    /// <summary>
    /// Whether a sign-in through this flow requires the account's phone number to be confirmed. On by
    /// default — a phone account's number is confirmed the moment its one-time code is verified, so this
    /// only matters for edge cases (for example a phone number attached to an otherwise-unconfirmed
    /// account by an administrator).
    /// </summary>
    public bool RequireConfirmedPhoneNumber { get; set; } = true;

    /// <summary>The number of failed one-time-code sign-in attempts that triggers a lockout, for this flow only.</summary>
    public int MaxFailedAccessAttempts { get; set; } = 5;

    /// <summary>How long an account stays locked out once <see cref="MaxFailedAccessAttempts"/> is reached.</summary>
    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Whether a newly created user is subject to this flow's lockout policy.</summary>
    public bool AllowedForNewUsers { get; set; } = true;

    void IHuiaOptionsSection.Validate(string path, List<string> errors)
    {
        if (!string.IsNullOrWhiteSpace(DefaultCountry))
        {
            errors.Require(DefaultCountry.Length == 2 && DefaultCountry.All(static c => c is >= 'A' and <= 'Z'),
                HuiaOptionsValidation.Combine(path, nameof(DefaultCountry)),
                "must be a two-letter upper-case ISO 3166-1 alpha-2 code.");
        }

        errors.Require(CodeLength is >= 4 and <= 10, HuiaOptionsValidation.Combine(path, nameof(CodeLength)),
            "must be between 4 and 10.");
        errors.Require(CodeLifetime > TimeSpan.Zero, HuiaOptionsValidation.Combine(path, nameof(CodeLifetime)),
            "must be positive.");
        errors.Require(MaxVerificationAttempts is >= 1 and <= 20,
            HuiaOptionsValidation.Combine(path, nameof(MaxVerificationAttempts)), "must be between 1 and 20.");
        errors.Require(PendingSignupLifetime >= CodeLifetime,
            HuiaOptionsValidation.Combine(path, nameof(PendingSignupLifetime)),
            "must be at least CodeLifetime.");
        errors.Require(ResendCooldown >= TimeSpan.Zero, HuiaOptionsValidation.Combine(path, nameof(ResendCooldown)),
            "must not be negative.");
        errors.Require(SuccessfulLoginsPerWindow >= 1,
            HuiaOptionsValidation.Combine(path, nameof(SuccessfulLoginsPerWindow)), "must be at least 1.");
        errors.Require(SuccessfulLoginWindow > TimeSpan.Zero,
            HuiaOptionsValidation.Combine(path, nameof(SuccessfulLoginWindow)), "must be positive.");
        errors.Require(SuccessfulLoginsPerDay >= SuccessfulLoginsPerWindow,
            HuiaOptionsValidation.Combine(path, nameof(SuccessfulLoginsPerDay)),
            "must be at least SuccessfulLoginsPerWindow.");
        errors.Require(MaxFailedAccessAttempts >= 1,
            HuiaOptionsValidation.Combine(path, nameof(MaxFailedAccessAttempts)), "must be at least 1.");
        errors.Require(LockoutDuration > TimeSpan.Zero,
            HuiaOptionsValidation.Combine(path, nameof(LockoutDuration)), "must be greater than zero.");
    }
}
