using Huia.HealthChecks;
using Huia.Multitenancy;
using Huia.OpenId.Options;
using Huia.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenIddict.Abstractions;

namespace Huia.OpenId.HealthChecks;

/// <summary>
/// Contributes OpenID client and scope existence checks to the Huia readiness health probe.
/// </summary>
internal sealed class HuiaOpenIdReadinessContributor(
    IServiceProvider services,
    HuiaOptions options) : IHuiaReadinessContributor
{
    public async Task<HealthCheckResult> CheckReadinessAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var applicationManager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var scopeManager = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();

        var missingClients = new List<string>();
        var clientCount = 0;
        var missingScopes = new List<string>();
        var scopeCount = 0;

        foreach (var (tenantId, tenant) in options.Tenants)
        {
            var openId = tenant.GetHuiaOpenId();
            if (openId is null)
            {
                continue;
            }

            foreach (var client in openId.Clients)
            {
                clientCount++;
                using (HuiaTenantScope.Enter(scope.ServiceProvider, tenantId))
                {
                    if (await applicationManager.FindByClientIdAsync(client.ClientId, cancellationToken) is null)
                    {
                        missingClients.Add(client.ClientId);
                    }
                }
            }

            foreach (var scopeDescriptor in openId.Scopes)
            {
                scopeCount++;
                using (HuiaTenantScope.Enter(scope.ServiceProvider, tenantId))
                {
                    if (await scopeManager.FindByNameAsync(scopeDescriptor.Name, cancellationToken) is null)
                    {
                        missingScopes.Add(scopeDescriptor.Name);
                    }
                }
            }
        }

        if (missingClients.Count == 0 && missingScopes.Count == 0)
        {
            return HealthCheckResult.Healthy(
                $"{clientCount} client(s), {scopeCount} scope(s) seeded.");
        }

        var data = new Dictionary<string, object>(StringComparer.Ordinal);
        if (missingClients.Count > 0)
        {
            data["missing-clients"] = string.Join(", ", missingClients);
        }

        if (missingScopes.Count > 0)
        {
            data["missing-scopes"] = string.Join(", ", missingScopes);
        }

        return HealthCheckResult.Unhealthy("Huia start-up seeding is incomplete.", data: data);
    }
}
