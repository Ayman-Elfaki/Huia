using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Quartz;

namespace Huia.Scheduling;

/// <summary>
/// Configures the Quartz job that prunes OpenIddict's own orphaned authorizations/tokens. Reachable via
/// <c>HuiaOptions.Scheduler</c> inside <c>services.AddHuia(issuer, huia => {...})</c>.
/// </summary>
/// <remarks>
/// This only has an effect once OpenIddict's Quartz.NET integration is actually registered — done for you by
/// <c>WithEntityFrameworkStores</c>/<c>WithStore</c> (from <c>Huia.EntityFrameworkCore</c>), which is what
/// resolves <see cref="OpenIddictQuartzOptions"/> and schedules the pruning job against it. Call order
/// relative to that doesn't matter: each method here just adds an <c>IServiceCollection.Configure&lt;
/// OpenIddictQuartzOptions&gt;</c> registration, and those compose regardless of whether they're added
/// before or after the integration that ends up reading them.
/// </remarks>
public sealed class SchedulerBuilder(IServiceCollection services)
{
    /// <summary>
    /// Amends the default OpenIddict Quartz.NET pruning configuration directly, for options not exposed by
    /// one of this builder's own methods.
    /// </summary>
    public void Configure(Action<OpenIddictQuartzOptions> configure) => services.Configure(configure);

    /// <summary>
    /// Disables pruning of orphaned authorizations entirely — they accumulate in storage indefinitely.
    /// </summary>
    public void DisableAuthorizationPruning() =>
        services.Configure<OpenIddictQuartzOptions>(options => options.DisableAuthorizationPruning = true);

    /// <summary>
    /// Disables pruning of orphaned tokens entirely — they accumulate in storage indefinitely.
    /// </summary>
    public void DisableTokenPruning() =>
        services.Configure<OpenIddictQuartzOptions>(options => options.DisableTokenPruning = true);

    /// <summary>
    /// Sets the number of times a failed pruning job run can be retried. OpenIddict's own default is 2.
    /// </summary>
    public void SetMaximumRefireCount(int count) =>
        services.Configure<OpenIddictQuartzOptions>(options => options.MaximumRefireCount = count);

    /// <summary>
    /// Sets the minimum lifespan an authorization must have (since its creation) before it's eligible for
    /// pruning. OpenIddict's own default is 14 days; it cannot be set below 10 minutes.
    /// </summary>
    public void SetMinimumAuthorizationLifespan(TimeSpan lifespan) =>
        services.Configure<OpenIddictQuartzOptions>(options => options.MinimumAuthorizationLifespan = lifespan);

    /// <summary>
    /// Sets the minimum lifespan a token must have (since its creation) before it's eligible for pruning.
    /// OpenIddict's own default is 14 days; it cannot be set below 10 minutes.
    /// </summary>
    public void SetMinimumTokenLifespan(TimeSpan lifespan) =>
        services.Configure<OpenIddictQuartzOptions>(options => options.MinimumTokenLifespan = lifespan);
}
