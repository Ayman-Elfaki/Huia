namespace Huia.Options;

/// <summary>
/// SMS delivery settings for the passwordless phone flow. Configured at the root
/// (<see cref="HuiaOptions.Sms"/>) and optionally overridden per tenant (<see cref="TenantOptions.Sms"/>).
/// </summary>
public sealed class SmsOptions : IHuiaOptionsSection
{
    /// <summary>The SMS provider key (for example <c>twilio</c>). Required for messages to be sent.</summary>
    public string? Provider { get; set; }

    /// <summary>Provider API key / account SID.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Provider API secret / auth token.</summary>
    public string? ApiSecret { get; set; }

    /// <summary>The sender ID or originating number shown to recipients.</summary>
    public string? SenderId { get; set; }

    /// <summary>
    /// When <see langword="true"/>, a configured provider also logs the plain code at Information level.
    /// Intended for local development only. OR-merged: set at either level enables it.
    /// </summary>
    public bool LogCodesToLogger { get; set; }

    /// <summary>Per-number one-time-code throttling. Not field-merged (see <see cref="MergedWith"/>).</summary>
    public OtpRateLimitOptions RateLimit { get; set; } = new();

    /// <summary>Whether a usable provider configuration is present.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Provider);

    /// <summary>
    /// Produces the effective options for a tenant. Scalar fields fall back to the root value when unset.
    /// <see cref="RateLimit"/> is <em>not</em> field-merged: the tenant's instance is kept only when the
    /// tenant also set its own <see cref="Provider"/>, otherwise the root's limit is used wholesale.
    /// </summary>
    /// <param name="tenant">The tenant-level overrides, or <see langword="null"/>.</param>
    /// <returns>A new, fully-populated <see cref="SmsOptions"/>.</returns>
    public SmsOptions MergedWith(SmsOptions? tenant)
    {
        if (tenant is null)
        {
            return Clone();
        }

        var tenantSetProvider = !string.IsNullOrWhiteSpace(tenant.Provider);
        return new SmsOptions
        {
            Provider = tenant.Provider ?? Provider,
            ApiKey = tenant.ApiKey ?? ApiKey,
            ApiSecret = tenant.ApiSecret ?? ApiSecret,
            SenderId = tenant.SenderId ?? SenderId,
            LogCodesToLogger = LogCodesToLogger || tenant.LogCodesToLogger,
            RateLimit = tenantSetProvider ? tenant.RateLimit.Clone() : RateLimit.Clone(),
        };
    }

    private SmsOptions Clone() => new()
    {
        Provider = Provider,
        ApiKey = ApiKey,
        ApiSecret = ApiSecret,
        SenderId = SenderId,
        LogCodesToLogger = LogCodesToLogger,
        RateLimit = RateLimit.Clone(),
    };

    void IHuiaOptionsSection.Validate(string path, List<string> errors)
    {
        ((IHuiaOptionsSection)RateLimit).Validate(HuiaOptionsValidation.Combine(path, nameof(RateLimit)), errors);

        if (!string.IsNullOrWhiteSpace(Provider))
        {
            errors.Require(!string.IsNullOrWhiteSpace(ApiKey),
                HuiaOptionsValidation.Combine(path, nameof(ApiKey)),
                "is required when a Provider is configured.");
        }
    }
}

/// <summary>
/// Fixed-window throttle applied per phone number to one-time-code requests. Enforced with
/// <c>System.Threading.RateLimiting</c>, never a cache.
/// </summary>
public sealed class OtpRateLimitOptions : IHuiaOptionsSection
{
    /// <summary>Maximum code requests permitted per number within <see cref="Window"/>.</summary>
    public int PermitLimit { get; set; } = 3;

    /// <summary>The length of the fixed window.</summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Absolute ceiling on code requests per number per rolling 24 hours.</summary>
    public int MaxPerNumberPerDay { get; set; } = 10;

    internal OtpRateLimitOptions Clone() => new()
    {
        PermitLimit = PermitLimit,
        Window = Window,
        MaxPerNumberPerDay = MaxPerNumberPerDay,
    };

    void IHuiaOptionsSection.Validate(string path, List<string> errors)
    {
        errors.Require(PermitLimit > 0, HuiaOptionsValidation.Combine(path, nameof(PermitLimit)), "must be greater than zero.");
        errors.Require(Window > TimeSpan.Zero, HuiaOptionsValidation.Combine(path, nameof(Window)), "must be positive.");
        errors.Require(MaxPerNumberPerDay >= PermitLimit,
            HuiaOptionsValidation.Combine(path, nameof(MaxPerNumberPerDay)),
            "must be at least PermitLimit.");
    }
}
