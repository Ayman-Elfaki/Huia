using Huia.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using OpenIddict.Server;
using Quartz;

namespace Huia.Keys;

/// <summary>
/// Enables Huia's signing/encryption key management, automatic or manual. Reachable via
/// <c>HuiaOptions.KeysManagement</c> inside <c>services.AddHuia(issuer, huia => {...})</c>.
/// </summary>
/// <remarks>
/// Either mode requires an <see cref="ISigningKeyStore"/> to be registered — chain
/// <c>.WithEntityFrameworkStores&lt;TContext&gt;()</c> (from <c>Huia.EntityFrameworkCore</c>),
/// <c>.WithStore&lt;TStore,...&gt;()</c>, or <c>.WithSigningKeyStore&lt;TKeyStore&gt;()</c> onto the
/// <see cref="HuiaBuilder"/> <c>AddHuia</c> returns. Call order between that and
/// <see cref="UseAutomaticKeyManagement"/>/<see cref="UseManualKeyManagement"/> doesn't matter — both only
/// need the store registered by the time the app actually starts.
/// </remarks>
public sealed class KeyManagementBuilder(IServiceCollection services)
{
    /// <summary>
    /// Enables automatic signing/encryption key rotation: a new key is generated ahead of the current one's
    /// retirement so there is never a gap, and retired keys stay valid for a grace period before being
    /// purged. <paramref name="configureQuartz"/> customizes the underlying
    /// Quartz scheduler (e.g. a persistent job store for multi-instance deployments) — it composes with any
    /// Quartz configuration passed to <c>WithEntityFrameworkStores</c>/<c>WithStore</c>, since Quartz's own
    /// <c>AddQuartz</c> is additive across calls. Call <see cref="UseManualKeyManagement"/> instead if you'd
    /// rather create/retire keys yourself, on your own trigger, without Quartz or a lifetime policy.
    /// </summary>
    /// <remarks>
    /// The periodic rotation check runs as a Quartz job, but this method doesn't itself start Quartz's
    /// hosted service — <c>WithEntityFrameworkStores</c> and <c>WithStore</c> already do, for OpenIddict's
    /// own record pruning. If you're backing key storage with a non-EF-Core <see cref="ISigningKeyStore"/>
    /// directly instead of going through <c>WithStore</c>, also call <c>services.AddQuartzHostedService()</c>
    /// yourself so this job actually runs.
    /// </remarks>
    public void UseAutomaticKeyManagement(
        Action<KeyManagementOptions>? configure = null,
        Action<IServiceCollectionQuartzConfigurator>? configureQuartz = null)
    {
        AddKeyManagementServices(configure);
        services.AddHostedService<KeyManagementInitializer>();

        services.AddQuartz(configureQuartz);

        services.AddQuartz((quartz, provider) =>
        {
            var keyManagement = provider.GetRequiredService<KeyManagementOptions>();

            var jobKey = new JobKey("Huia.KeyRotation");
            quartz.AddJob<KeyRotationJob>(job => job.WithIdentity(jobKey).StoreDurably());
            quartz.AddTrigger(trigger => trigger
                .ForJob(jobKey)
                .WithIdentity("Huia.KeyRotation-trigger")
                .WithCronSchedule(keyManagement.RotationCronExpression));
        });
    }

    /// <summary>
    /// Enables signing/encryption key management without automatic rotation: the app itself decides when a
    /// key is created or retired, by calling <see cref="KeyManager.CreateKeyAsync"/>/
    /// <see cref="KeyManager.RetireKeyAsync"/> (e.g. from an admin action, a CLI command, or its own
    /// schedule) rather than <see cref="KeyManagementOptions"/>'s lead-time/lifetime policy. No Quartz job
    /// is registered.
    /// </summary>
    public void UseManualKeyManagement(Action<KeyManagementOptions>? configure = null)
    {
        AddKeyManagementServices(configure);
        services.AddHostedService<ManualKeyManagementInitializer>();
    }

    private void AddKeyManagementServices(Action<KeyManagementOptions>? configure)
    {
        // WithEntityFrameworkStores may have already registered a default-valued instance (the EF signing
        // key store needs one regardless of whether rotation is enabled) — reuse it instead of shadowing it
        // with a second registration, so configuration here applies however the two are called relative to each other
        var options = services.FirstOrDefault(d => d.ServiceType == typeof(KeyManagementOptions))?
                .ImplementationInstance as KeyManagementOptions ?? new KeyManagementOptions();
        
        configure?.Invoke(options);
        services.TryAddSingleton(options);

        // Unlike the options/HuiaKeyManager registrations above, these two must not use TryAdd: OpenIddict's
        // own AddServer() already registers other IConfigureOptions<OpenIddictServerOptions> instances (its
        // own defaults), and TryAddSingleton no-ops the moment *any* registration exists for that service
        // type — it would silently drop Huia's configurator rather than composing alongside the others.
        services.TryAddSingleton<KeyManager>();
        services.AddSingleton<IConfigureOptions<OpenIddictServerOptions>, ServerOptionsConfigurator>();
        services.AddSingleton<IOptionsChangeTokenSource<OpenIddictServerOptions>, KeyChangeTokenSource>();
    }
}