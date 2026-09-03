namespace Huia.Options;

/// <summary>
/// Per-tenant account-lockout policy. Projected onto <c>IdentityOptions.Lockout</c> for the tenant by
/// <c>AddHuiaPerTenantIdentityOptions</c>; the process-global registration keeps the least restrictive
/// values as the default.
/// </summary>
public sealed class TenantLockoutOptions : IHuiaOptionsSection
{
    /// <summary>The number of failed access attempts that triggers a lockout.</summary>
    public int MaxFailedAccessAttempts { get; set; } = 5;

    /// <summary>How long an account stays locked out once the attempt limit is reached.</summary>
    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Whether a newly created user is subject to lockout.</summary>
    public bool AllowedForNewUsers { get; set; } = true;

    void IHuiaOptionsSection.Validate(string path, List<string> errors)
    {
        errors.Require(MaxFailedAccessAttempts >= 1,
            HuiaOptionsValidation.Combine(path, nameof(MaxFailedAccessAttempts)), "must be at least 1.");
        errors.Require(LockoutDuration > TimeSpan.Zero,
            HuiaOptionsValidation.Combine(path, nameof(LockoutDuration)), "must be greater than zero.");
    }
}
