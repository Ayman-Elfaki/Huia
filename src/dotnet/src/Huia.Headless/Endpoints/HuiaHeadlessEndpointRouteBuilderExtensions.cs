using Huia.Headless.Endpoints;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.AspNetCore.Builder;

/// <summary>Extension methods for mapping Huia Headless API endpoints.</summary>
public static class HuiaHeadlessEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps the bearer-token headless identity endpoints (register, login, phone login, refresh, logout, email, password) under /{tenant}/identity/*.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The route group builder.</returns>
    public static RouteGroupBuilder MapHuiaHeadlessEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return HuiaHeadlessEndpoints.MapHuiaHeadlessEndpoints(endpoints);
    }
}
