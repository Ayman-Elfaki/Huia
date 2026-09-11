using Huia.Keys;
using Huia.Options;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Huia.HealthChecks;

/// <summary>
/// Reports the identity provider ready only when the database is reachable and the start-up seeding has
/// completed: every configured tenant has a usable signing key and any flavor-contributed checks pass.
/// </summary>
internal sealed class HuiaReadinessHealthCheck(
    IHuiaSigningKeyStore keyStore,
    HuiaOptions options,
    IEnumerable<IHuiaReadinessContributor> contributors) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await keyStore.CanConnectAsync(cancellationToken))
            {
                return HealthCheckResult.Unhealthy("The Huia database is unreachable.");
            }

            var keyedTenants = await keyStore.GetKeyedTenantsAsync(cancellationToken);

            var missingKeys = options.Tenants.Keys
                .Where(tenantId => !keyedTenants.Contains(tenantId, StringComparer.Ordinal))
                .ToList();

            if (missingKeys.Count > 0)
            {
                return HealthCheckResult.Unhealthy(
                    $"The following tenant(s) have no usable signing key: {string.Join(", ", missingKeys)}.");
            }

            foreach (var contributor in contributors)
            {
                var result = await contributor.CheckReadinessAsync(cancellationToken);
                if (result.Status != HealthStatus.Healthy)
                {
                    return result;
                }
            }

            return HealthCheckResult.Healthy("Huia is ready.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Huia readiness check threw an exception: {ex.Message}", ex);
        }
    }
}
