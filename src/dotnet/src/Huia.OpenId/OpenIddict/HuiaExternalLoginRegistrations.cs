using Huia.Multitenancy;
using Huia.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Client;

namespace Huia.OpenId.OpenIddict;

/// <summary>
/// Adds one OpenIddict client registration per (tenant, external provider), read from
/// <see cref="IHuiaTenantProvider"/> every time <see cref="OpenIddictClientOptions"/> is built. Clearing the
/// options cache (see <see cref="HuiaExternalLoginRefreshListener"/>) therefore makes a tenant created, edited or
/// removed at runtime take effect without a restart.
/// </summary>
/// <remarks>
/// The provider builders only know how to add registrations to a service collection, so they are run against a
/// scratch one and the registrations they configure are harvested. The web-integration post-configure step then
/// completes each registration (issuer, endpoints, defaults) exactly as it does for a start-up registration.
/// </remarks>
internal sealed class HuiaExternalLoginRegistrations(IHuiaTenantProvider tenants) : IConfigureOptions<OpenIddictClientOptions>
{
    public void Configure(OpenIddictClientOptions options)
    {
        var scratch = new ServiceCollection();
        HuiaOpenIddictConfiguration.RegisterExternalProviders(new OpenIddictClientBuilder(scratch), tenants);

        using var provider = scratch.BuildServiceProvider();
        var harvested = new OpenIddictClientOptions();
        foreach (var configure in provider.GetServices<IConfigureOptions<OpenIddictClientOptions>>())
        {
            configure.Configure(harvested);
        }

        foreach (var registration in harvested.Registrations)
        {
            options.Registrations.Add(registration);
        }
    }
}

/// <summary>
/// Rebuilds the OpenIddict client options when a tenant that carries external providers appears, or when a
/// persisted tenant is edited or removed, so its registrations are (re)generated from the tenant provider.
/// </summary>
internal sealed class HuiaExternalLoginRefreshListener(
    IOptionsMonitorCache<OpenIddictClientOptions> cache, IHuiaTenantProvider tenants) : IHuiaTenantChangeListener
{
    public Task OnTenantsAddedAsync(IReadOnlyCollection<string> tenantIds, CancellationToken cancellationToken)
    {
        if (tenantIds.Any(id => tenants.TryGetValue(id, out var tenant)
                                && tenant.Authentication.Find<ExternalLoginAuthenticationMethod>() is not null))
        {
            cache.Clear();
        }

        return Task.CompletedTask;
    }

    public Task OnTenantsChangedAsync(
        IReadOnlyCollection<string> changedTenantIds,
        IReadOnlyCollection<string> removedTenantIds,
        CancellationToken cancellationToken)
    {
        cache.Clear();
        return Task.CompletedTask;
    }
}
