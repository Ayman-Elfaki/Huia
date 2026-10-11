using Huia.Multitenancy;
using Huia.OpenId.Identity;
using Huia.OpenId.Keys;
using Huia.OpenId.OpenIddict;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Huia.OpenId.Multitenancy;

/// <summary>
/// Runs the per-tenant start-up work for tenants that appear at runtime: it mints each new tenant's signing
/// key and seeds its roles, scopes and clients, exactly as start-up does for the tenants known then.
/// </summary>
internal sealed partial class HuiaTenantBootstrapListener(
    IServiceProvider services,
    IHuiaTenantProvider tenants,
    HuiaClientSeeder clients,
    HuiaScopeSeeder scopes,
    HuiaRoleSeeder roles,
    ILogger<HuiaTenantBootstrapListener> logger) : IHuiaTenantChangeListener
{
    public async Task OnTenantsAddedAsync(IReadOnlyCollection<string> tenantIds, CancellationToken cancellationToken)
    {
        await using (var scope = services.CreateAsyncScope())
        {
            var lifecycle = scope.ServiceProvider.GetRequiredService<HuiaKeyLifecycleService>();
            await lifecycle.EnsureBootstrappedAsync(cancellationToken, tenantIds);
        }

        foreach (var tenantId in tenantIds)
        {
            if (!tenants.TryGetValue(tenantId, out var tenant))
            {
                continue;
            }

            await scopes.SeedTenantAsync(tenantId, tenant, cancellationToken);
            await clients.SeedTenantAsync(tenantId, tenant, cancellationToken);
            await roles.SeedTenantAsync(tenantId, tenant, cancellationToken);
            LogBootstrapped(tenantId);
        }
    }

    [LoggerMessage(LogLevel.Information, "Bootstrapped new tenant {TenantId}: signing key, scopes, clients and roles.")]
    partial void LogBootstrapped(string tenantId);
}
