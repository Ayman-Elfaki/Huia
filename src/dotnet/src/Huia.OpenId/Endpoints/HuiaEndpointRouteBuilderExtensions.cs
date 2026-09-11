using Finbuckle.MultiTenant.Abstractions;
using Huia.Multitenancy;
using Huia.OpenId.Endpoints;
using Huia.OpenId.UI;
using Huia.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.Builder;

/// <summary>Maps the Huia OpenID Connect HTTP endpoints. Call after <c>UseHuiaOpenId()</c>.</summary>
public static class HuiaOpenIdEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps every OpenID Connect endpoint group: common health/manage endpoints, OAuth/OIDC protocol
    /// endpoints, OpenId admin endpoints, and — when <c>AddHuiaUi()</c> was called — the Razor Pages account UI.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The same endpoint route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapHuiaOpenIdEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapHuiaEndpoints();
        endpoints.MapHuiaConnectEndpoints();
        endpoints.MapHuiaOpenIdManageEndpoints();

        if (endpoints.ServiceProvider.GetService<HuiaUiMarker>() is not null)
        {
            endpoints.MapRazorPages();
            endpoints.MapHuiaExternalEndpoints();
            endpoints.MapHuiaPasskeyEndpoints();
        }

        return endpoints;
    }

    /// <summary>Maps only the OAuth / OpenID Connect protocol endpoints.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The connect route group.</returns>
    public static RouteGroupBuilder MapHuiaConnectEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return ConnectEndpoints.MapHuiaConnectEndpoints(endpoints);
    }

    /// <summary>Maps the external-provider challenge and callback endpoints.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The external route group.</returns>
    public static RouteGroupBuilder MapHuiaExternalEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return ExternalEndpoints.MapHuiaExternalEndpoints(endpoints);
    }

    /// <summary>Maps the OpenID extensions to the self-service <c>/manage/*</c> API.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The manage route group.</returns>
    public static RouteGroupBuilder MapHuiaOpenIdManageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return ManageEndpointsOpenId.MapHuiaOpenIdManageEndpoints(endpoints);
    }

    /// <summary>Maps OpenID client, scope, and key admin endpoints.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The admin route group.</returns>
    public static RouteGroupBuilder MapHuiaOpenIdAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return AdminEndpointsOpenId.MapHuiaOpenIdAdminEndpoints(endpoints);
    }

    /// <summary>
    /// Maps a handler at <c>/</c> with account UI redirection support.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="fallbackTenantId">The tenant to redirect to when the request carries none.</param>
    /// <returns>The endpoint route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapHuiaOpenIdHome(this IEndpointRouteBuilder endpoints, string? fallbackTenantId = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/", (HttpContext context, IMultiTenantContextAccessor tenantAccessor, HuiaOptions options) =>
        {
            if (context.Request.PathBase.HasValue)
            {
                if (context.RequestServices.GetService(typeof(HuiaUiMarker)) is null)
                {
                    return Results.NoContent();
                }

                var page = context.User.Identity?.IsAuthenticated == true ? "signedin" : "login";
                var returnUrl = context.Request.Query["ReturnUrl"].ToString();
                var suffix = string.IsNullOrEmpty(returnUrl) ? string.Empty : $"?ReturnUrl={Uri.EscapeDataString(returnUrl)}";
                return Results.Redirect($"{context.Request.PathBase}/identity/account/{page}{suffix}");
            }

            var tenant = tenantAccessor.CurrentTenantId()
                ?? fallbackTenantId
                ?? options.Tenants.Keys.FirstOrDefault();
            return tenant is null ? Results.NotFound() : Results.Redirect($"/{tenant}/");
        });

        return endpoints;
    }
}
