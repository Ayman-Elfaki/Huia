using Finbuckle.MultiTenant.Abstractions;
using Huia.Endpoints;
using Huia.HealthChecks;
using Huia.Multitenancy;
using Huia.Options;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.AspNetCore.Builder;

/// <summary>Maps the common Huia HTTP endpoints. Call after <c>UseHuia()</c>.</summary>
public static class HuiaEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps common Huia endpoint groups: health checks and manage API.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The same endpoint route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapHuiaEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapHuiaHealthChecks();
        endpoints.MapHuiaManageEndpoints();

        return endpoints;
    }

    /// <summary>
    /// Maps the health probes at the application root: <c>/health/live</c> (process liveness, runs no
    /// checks) and <c>/health/ready</c> (readiness — 503 until the database is reachable and the Huia
    /// start-up seeding has completed). Both are anonymous.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The endpoint route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapHuiaHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(HuiaHealthChecksConfiguration.ReadyTag),
        });

        return endpoints;
    }

    /// <summary>Maps the token-protected self-service API (<c>/manage/*</c>), guarded by the <c>Huia:Api</c> policy.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The manage route group.</returns>
    public static RouteGroupBuilder MapHuiaManageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return ManageEndpoints.MapHuiaManageEndpoints(endpoints);
    }

    /// <summary>
    /// Maps the Admin endpoints (<c>/admin/*</c>) and returns the group <em>without</em> an
    /// authorization policy. The host must attach one.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The admin route group, for the host to call <c>.RequireAuthorization(...)</c> on.</returns>
    public static RouteGroupBuilder MapHuiaAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return AdminEndpoints.MapHuiaAdminEndpoints(endpoints);
    }

    /// <summary>
    /// Maps the Admin endpoints (<c>/admin/*</c>) onto an existing route group and returns the group
    /// <em>without</em> an authorization policy. The host must attach one.
    /// </summary>
    /// <param name="group">The route group builder.</param>
    /// <returns>The admin route group, for chaining.</returns>
    public static RouteGroupBuilder MapHuiaAdminEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return AdminEndpoints.MapHuiaAdminEndpoints(group);
    }

    /// <summary>
    /// Maps a handler at <c>/</c>. At the bare root it redirects to the tenant root (current tenant, else
    /// <paramref name="fallbackTenantId"/>, else the first configured tenant). At a tenant root
    /// (<c>/{tenant}/</c> after base-path rebasing) it hands off or returns 204.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="fallbackTenantId">The tenant to redirect to when the request carries none.</param>
    /// <returns>The endpoint route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapHuiaHome(this IEndpointRouteBuilder endpoints, string? fallbackTenantId = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/", (HttpContext context, IMultiTenantContextAccessor tenantAccessor, HuiaOptions options) =>
        {
            if (context.Request.PathBase.HasValue)
            {
                var uiMarkerType = Type.GetType("Huia.OpenId.UI.HuiaUiMarker, Huia.OpenId");
                if (uiMarkerType is not null && context.RequestServices.GetService(uiMarkerType) is not null)
                {
                    var page = context.User.Identity?.IsAuthenticated == true ? "signedin" : "login";
                    var returnUrl = context.Request.Query["ReturnUrl"].ToString();
                    var suffix = string.IsNullOrEmpty(returnUrl) ? string.Empty : $"?ReturnUrl={Uri.EscapeDataString(returnUrl)}";
                    return Results.Redirect($"{context.Request.PathBase}/identity/account/{page}{suffix}");
                }

                return Results.NoContent();
            }

            var tenant = tenantAccessor.CurrentTenantId()
                ?? fallbackTenantId
                ?? options.Tenants.Keys.FirstOrDefault();
            return tenant is null ? Results.NotFound() : Results.Redirect($"/{tenant}/");
        });

        return endpoints;
    }
}
