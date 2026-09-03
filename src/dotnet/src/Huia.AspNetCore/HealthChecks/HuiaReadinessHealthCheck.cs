using Huia.EntityFrameworkCore;
using Huia.EntityFrameworkCore.Entities;
using Huia.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenIddict.Abstractions;

namespace Huia.AspNetCore.HealthChecks;

/// <summary>
/// Reports the identity provider ready only when the database is reachable and the start-up seeding has
/// completed: every configured tenant has a usable signing key (mirrors <c>HuiaKeyLifecycleService</c>)
/// and every configured OAuth client exists in the store (mirrors <c>HuiaClientSeeder</c>). Any missing
/// piece — or an unreachable / unmigrated database — is <see cref="HealthStatus.Unhealthy"/>.
/// </summary>
internal sealed class HuiaReadinessHealthCheck(
    HuiaDbContext dbContext,
    IOpenIddictApplicationManager applicationManager,
    IOpenIddictScopeManager scopeManager,
    HuiaOptions options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await dbContext.Database.CanConnectAsync(cancellationToken))
            {
                return HealthCheckResult.Unhealthy("The Huia database is unreachable.");
            }

            var keyedTenants = await dbContext.SigningKeys
                .Where(k => k.Status == HuiaSigningKeyStatus.Active || k.Status == HuiaSigningKeyStatus.Pending)
                .Select(k => k.TenantId)
                .Distinct()
                .ToListAsync(cancellationToken);

            var missingKeys = options.Tenants.Keys
                .Where(tenantId => !keyedTenants.Contains(tenantId, StringComparer.Ordinal))
                .ToList();

            var missingClients = new List<string>();
            var clientCount = 0;
            var missingScopes = new List<string>();
            var scopeCount = 0;
            foreach (var tenant in options.Tenants.Values)
            {
                foreach (var client in tenant.Clients)
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
                    $"Database reachable; {options.Tenants.Count} tenant(s) keyed; {clientCount} client(s), {scopeCount} scope(s) seeded.");
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
