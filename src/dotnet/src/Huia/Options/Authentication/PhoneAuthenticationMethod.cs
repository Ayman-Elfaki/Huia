namespace Huia.Options;

/// <summary>
/// Passwordless phone (SMS one-time code) authentication method options.
/// </summary>
public class PhoneAuthenticationMethod : HuiaAuthenticationMethod
{
    /// <inheritdoc />
    public override string MethodType => "Phone";

    /// <summary>Default phone region (ISO 3166-1 alpha-2, e.g. "US").</summary>
    public string? DefaultCountry { get; set; }

    /// <summary>Whether an unknown valid phone number creates an account automatically.</summary>
    public bool AllowAutoProvisioning { get; set; } = false;

    /// <summary>Length of one-time code digits. Default is 6.</summary>
    public int OtpLength { get; set; } = 6;

    /// <summary>Alias for OtpLength.</summary>
    public int CodeLength
    {
        get => OtpLength;
        set => OtpLength = value;
    }

    /// <summary>Lifespan of one-time code. Default is 5 minutes.</summary>
    public TimeSpan OtpLifespan { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Alias for OtpLifespan.</summary>
    public TimeSpan CodeLifetime
    {
        get => OtpLifespan;
        set => OtpLifespan = value;
    }

    /// <summary>Maximum verification attempts against a single issued code before it is invalidated.</summary>
    public int MaxVerificationAttempts { get; set; } = 5;

    /// <summary>How long a pending auto-provisioning record is retained.</summary>
    public TimeSpan PendingSignupLifetime { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>When CAPTCHA challenge is presented.</summary>
    public CaptchaMode Captcha { get; set; } = CaptchaMode.None;

    /// <summary>Minimum wait before "send a new code" is offered again.</summary>
    public TimeSpan ResendCooldown { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Number of successful sign-ins permitted in a rolling window.</summary>
    public int SuccessfulLoginsPerWindow { get; set; } = 1;

    /// <summary>Duration of rolling successful sign-in window.</summary>
    public TimeSpan SuccessfulLoginWindow { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Maximum successful sign-ins permitted per calendar day.</summary>
    public int SuccessfulLoginsPerDay { get; set; } = 5;

    /// <summary>Whether a sign-in through this flow requires the account's phone number to be confirmed.</summary>
    public bool RequireConfirmedPhoneNumber { get; set; } = true;

    /// <summary>Maximum failed attempts before account is locked out.</summary>
    public int MaxFailedAccessAttempts { get; set; } = 5;

    /// <summary>Lockout duration.</summary>
    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Whether a newly created user is subject to this flow's lockout policy.</summary>
    public bool AllowedForNewUsers { get; set; } = true;

    /// <inheritdoc />
    public override void Validate(string path, List<string> errors)
    {
        base.Validate(path, errors);

        if (!string.IsNullOrWhiteSpace(DefaultCountry))
        {
            errors.Require(DefaultCountry.Length == 2 && DefaultCountry.All(static c => c is >= 'A' and <= 'Z'),
                HuiaOptionsValidation.Combine(path, nameof(DefaultCountry)),
                "must be a 2-letter uppercase ISO 3166-1 alpha-2 code (e.g. 'US').");
        }

        errors.Require(OtpLength is >= 4 and <= 10,
            HuiaOptionsValidation.Combine(path, nameof(OtpLength)),
            "must be between 4 and 10 digits.");
        errors.Require(OtpLifespan > TimeSpan.Zero,
            HuiaOptionsValidation.Combine(path, nameof(OtpLifespan)),
            "must be greater than zero.");
        errors.Require(SuccessfulLoginsPerWindow >= 1,
            HuiaOptionsValidation.Combine(path, nameof(SuccessfulLoginsPerWindow)),
            "must be at least 1.");
        errors.Require(SuccessfulLoginWindow > TimeSpan.Zero,
            HuiaOptionsValidation.Combine(path, nameof(SuccessfulLoginWindow)),
            "must be greater than zero.");
        errors.Require(SuccessfulLoginsPerDay >= SuccessfulLoginsPerWindow,
            HuiaOptionsValidation.Combine(path, nameof(SuccessfulLoginsPerDay)),
            "must be at least SuccessfulLoginsPerWindow.");
        errors.Require(MaxFailedAccessAttempts >= 1,
            HuiaOptionsValidation.Combine(path, nameof(MaxFailedAccessAttempts)),
            "must be at least 1.");
        errors.Require(LockoutDuration > TimeSpan.Zero,
            HuiaOptionsValidation.Combine(path, nameof(LockoutDuration)),
            "must be greater than zero.");
    }
}
