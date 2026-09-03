using Microsoft.Extensions.DependencyInjection;

namespace Huia.AspNetCore.HealthChecks;

/// <summary>Registers the Huia readiness health check. Mapped by <c>MapHuiaEndpoints()</c>.</summary>
internal static class HuiaHealthChecksConfiguration
{
    /// <summary>The registered name of the Huia readiness check.</summary>
    public const string ReadinessCheckName = "huia";

    /// <summary>The tag applied to readiness checks, filtered by the <c>/health/ready</c> endpoint.</summary>
    public const string ReadyTag = "ready";

    public static IServiceCollection AddHuiaHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<HuiaReadinessHealthCheck>(ReadinessCheckName, tags: [ReadyTag]);

        return services;
    }
}
