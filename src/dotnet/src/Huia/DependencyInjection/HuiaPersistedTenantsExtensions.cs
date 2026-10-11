using Huia.DependencyInjection;
using Huia.Multitenancy;
using Huia.Tenants;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Opt-in registration of runtime-defined (persisted) tenants.</summary>
public static class HuiaPersistedTenantsExtensions
{
    /// <summary>
    /// Serves persisted tenants alongside the code-first ones: <see cref="IHuiaTenantProvider"/> becomes a
    /// snapshot of the options tree plus everything in <see cref="Huia.Stores.IHuiaTenantStore"/>, refreshed at
    /// start-up, on <see cref="IHuiaTenantRefresher.RefreshAsync"/> and every minute. A store adapter such as
    /// <c>AddEntityFrameworkCoreStores</c> must be registered.
    /// </summary>
    /// <param name="builder">The Huia builder.</param>
    /// <returns>The builder, for chaining.</returns>
    public static IHuiaBuilder AddPersistedTenants(this IHuiaBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;
        services.TryAddSingleton<HuiaTenantSerializer>();

        // The shared IHuiaTenantProvider is a holder that registration-time wiring already captured; the
        // store-backed provider installs itself behind it the moment it is constructed (at host start).
        services.AddSingleton<HuiaStoredTenantProvider>(sp =>
        {
            var provider = ActivatorUtilities.CreateInstance<HuiaStoredTenantProvider>(sp);
            provider.Install();
            return provider;
        });
        services.AddSingleton<IHuiaTenantRefresher>(sp => sp.GetRequiredService<HuiaStoredTenantProvider>());
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<HuiaStoredTenantProvider>());

        return builder;
    }
}
