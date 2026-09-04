namespace Huia.Options;

/// <summary>
/// Controls the OpenIddict.Quartz background job that prunes orphaned and expired authorizations and
/// tokens. The job itself runs hourly (with a short random startup jitter) — that interval is fixed by
/// OpenIddict.Quartz and is not configurable.
/// </summary>
public sealed class CleanupOptions : IHuiaOptionsSection
{
    /// <summary>
    /// Whether the cleanup job runs at all. On by default — without it, expired authorizations and
    /// tokens accumulate forever. Turn off in tests.
    /// </summary>
    public bool EnableBackgroundJobs { get; set; } = true;

    /// <summary>Whether orphaned/expired authorizations are pruned.</summary>
    public bool PruneAuthorizations { get; set; } = true;

    /// <summary>Whether orphaned/expired tokens are pruned.</summary>
    public bool PruneTokens { get; set; } = true;

    /// <summary>
    /// How long an authorization must have been inactive before it's eligible for pruning — a guard
    /// against deleting one still in use by a long-lived flow.
    /// </summary>
    public TimeSpan MinimumAuthorizationLifespan { get; set; } = TimeSpan.FromDays(14);

    /// <summary>Same guard as <see cref="MinimumAuthorizationLifespan"/>, for tokens.</summary>
    public TimeSpan MinimumTokenLifespan { get; set; } = TimeSpan.FromDays(14);

    /// <summary>How many times a cleanup run that didn't finish in one Quartz firing may be re-queued.</summary>
    public int MaximumRefireCount { get; set; } = 2;

    void IHuiaOptionsSection.Validate(string path, List<string> errors)
    {
        // OpenIddict.Quartz's own SetMinimum*Lifespan throw below 10 minutes; validate up front for a
        // clear HuiaOptionsException instead of a raw ArgumentOutOfRangeException from deep in AddHuia.
        errors.Require(MinimumAuthorizationLifespan >= TimeSpan.FromMinutes(10),
            HuiaOptionsValidation.Combine(path, nameof(MinimumAuthorizationLifespan)), "must be at least 10 minutes.");
        errors.Require(MinimumTokenLifespan >= TimeSpan.FromMinutes(10),
            HuiaOptionsValidation.Combine(path, nameof(MinimumTokenLifespan)), "must be at least 10 minutes.");
        errors.Require(MaximumRefireCount >= 0,
            HuiaOptionsValidation.Combine(path, nameof(MaximumRefireCount)), "must not be negative.");
    }
}
