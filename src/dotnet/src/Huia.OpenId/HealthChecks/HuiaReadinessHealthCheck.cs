using Huia.Multitenancy;
using Huia.OpenId.Entities;
using Huia.Options;
using Huia.Stores;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenIddict.Abstractions;

namespace Huia.OpenId.HealthChecks;

/// <summary>
/// Reports the identity provider ready only when the database is reachable and the start-up seeding has
/// completed: every configured tenant has a usable signing key (mirrors <c>HuiaKeyLifecycleService</c>)
/// and every configured OAuth client exists in the store (mirrors <c>HuiaClientSeeder</c>). Any missing
/// piece — or an unreachable / unmigrated database — is <see cref="HealthStatus.Unhealthy"/>.
/// </summary>
internal sealed class HuiaReadinessHealthCheck(
    IHuiaDatabaseProbe databaseProbe,
    IHuiaSigningKeyStore signingKeys,
    IOpenIddictApplicationManager applicationManager,
    IOpenIddictScopeManager scopeManager,
    IHuiaTenantProvider tenants) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await databaseProbe.CanConnectAsync(cancellationToken))
            {
                return HealthCheckResult.Unhealthy("The Huia database is unreachable.");
            }

            var usableKeys = await signingKeys.QueryAsync(
                new HuiaSigningKeyFilter
                {
                    Statuses = [HuiaSigningKeyStatus.Active, HuiaSigningKeyStatus.Pending],
                },
                cancellationToken);
            var keyedTenants = usableKeys.Select(k => k.TenantId).Distinct(StringComparer.Ordinal).ToList();

            var missingKeys = tenants.Keys
                .Where(tenantId => !keyedTenants.Contains(tenantId, StringComparer.Ordinal))
                .ToList();

            var missingClients = new List<string>();
            var clientCount = 0;
            var missingScopes = new List<string>();
            var scopeCount = 0;
            foreach (var tenant in tenants.Values)
            {
                foreach (var client in tenant.Applications)
                {
                    clientCount++;
                    if (await applicationManager.FindByClientIdAsync(client.ClientId, cancellationToken) is null)
                    {
                        missingClients.Add(client.ClientId);
                    }
                }

                foreach (var scope in tenant.Scopes)
                {
                    scopeCount++;
                    if (await scopeManager.FindByNameAsync(scope.Name, cancellationToken) is null)
                    {
                        missingScopes.Add(scope.Name);
                    }
                }
            }

            if (missingKeys.Count == 0 && missingClients.Count == 0 && missingScopes.Count == 0)
            {
                return HealthCheckResult.Healthy(
                    $"Database reachable; {tenants.Count} tenant(s) keyed; {clientCount} client(s), {scopeCount} scope(s) seeded.");
            }

            var data = new Dictionary<string, object>(StringComparer.Ordinal);
            if (missingKeys.Count > 0)
            {
                data["missing-signing-keys"] = string.Join(", ", missingKeys);
            }

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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("The Huia readiness check threw.", ex);
        }
    }
}
