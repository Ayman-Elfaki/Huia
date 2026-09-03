namespace Huia.Options;

/// <summary>
/// Controls the per-tenant signing-key lifecycle driven by the Quartz jobs. Private keys are wrapped
/// with ASP.NET Core Data Protection at rest.
/// </summary>
public sealed class KeyManagementOptions : IHuiaOptionsSection
{
    /// <summary>How often a fresh signing key is generated for each tenant.</summary>
    public TimeSpan RotationInterval { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Delay between a key being created and it becoming the active signing key. It is published in the
    /// JWKS during this window so relying parties can pick it up before it signs anything.
    /// </summary>
    public TimeSpan ActivationDelay { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// How long a rotated-out key remains published (able to validate existing tokens) after it stops
    /// signing, before it is retired.
    /// </summary>
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(7);

    /// <summary>How long a retired key is kept (unpublished) before it is permanently deleted.</summary>
    public TimeSpan RetiredKeyGracePeriod { get; set; } = TimeSpan.FromDays(7);

    /// <summary>RSA key size in bits for newly generated keys.</summary>
    public int KeySize { get; set; } = 2048;

    /// <summary>
    /// Whether the Quartz key-lifecycle jobs (rotation, activation, retirement, deletion) are scheduled.
    /// The start-up bootstrap of a first key always runs regardless. Turn off in tests.
    /// </summary>
    public bool EnableBackgroundJobs { get; set; } = true;

    void IHuiaOptionsSection.Validate(string path, List<string> errors)
    {
        errors.Require(RotationInterval > TimeSpan.Zero, HuiaOptionsValidation.Combine(path, nameof(RotationInterval)), "must be positive.");
        errors.Require(ActivationDelay >= TimeSpan.Zero, HuiaOptionsValidation.Combine(path, nameof(ActivationDelay)), "must not be negative.");
        errors.Require(RetentionPeriod > TimeSpan.Zero, HuiaOptionsValidation.Combine(path, nameof(RetentionPeriod)), "must be positive.");
        errors.Require(RetiredKeyGracePeriod >= TimeSpan.Zero, HuiaOptionsValidation.Combine(path, nameof(RetiredKeyGracePeriod)), "must not be negative.");
        errors.Require(KeySize is 2048 or 3072 or 4096, HuiaOptionsValidation.Combine(path, nameof(KeySize)), "must be 2048, 3072 or 4096.");
        errors.Require(ActivationDelay < RotationInterval, HuiaOptionsValidation.Combine(path, nameof(ActivationDelay)),
            "must be shorter than RotationInterval.");
    }
}
