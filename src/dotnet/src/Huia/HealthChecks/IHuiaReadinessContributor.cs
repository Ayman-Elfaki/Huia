using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Huia.HealthChecks;

/// <summary>Allows identity flavors (such as OpenId) to contribute extra readiness checks.</summary>
public interface IHuiaReadinessContributor
{
    /// <summary>Performs the flavor-specific readiness check.</summary>
    Task<HealthCheckResult> CheckReadinessAsync(CancellationToken cancellationToken);
}
