using Huia.AspNetCore.Endpoints;
using Huia.AspNetCore.HealthChecks;
using Huia.AspNetCore.UI;
using Huia.EntityFrameworkCore.Multitenancy;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.Builder;

/// <summary>Maps the Huia HTTP endpoints. Call after <c>UseHuia()</c>.</summary>
public static class HuiaEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps every Huia endpoint group: the OAuth/OIDC protocol endpoints and — when <c>AddHuiaUi()</c>
    /// was called — the Razor Pages account UI.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The same endpoint route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapHuiaEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapHuiaHealthChecks();
        endpoints.MapHuiaConnectEndpoints();
        endpoints.MapHuiaManageEndpoints();

        if (endpoints.ServiceProvider.GetService<HuiaUiMarker>() is not null)
        {
            endpoints.MapRazorPages();
            endpoints.MapHuiaExternalEndpoints();
            endpoints.MapHuiaPasskeyEndpoints();
        }

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
    /// Maps a handler at <c>/</c>. At the bare root it redirects to the tenant root (current tenant, else
    /// <paramref name="fallbackTenantId"/>, else the first configured tenant). At a tenant root
    /// (<c>/{tenant}/</c> after base-path rebasing) it sends the caller to the account UI — an anonymous
    /// one to sign-in, an authenticated one to a "you're signed in" confirmation — never a redirect back
    /// to itself. When the account UI is not registered it answers <c>204 No Content</c>.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="fallbackTenantId">The tenant to redirect to when the request carries none.</param>
    /// <returns>The endpoint route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapHuiaHome(this IEndpointRouteBuilder endpoints, string? fallbackTenantId = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/", (Http.HttpContext context, Finbuckle.MultiTenant.Abstractions.IMultiTenantContextAccessor tenantAccessor, Huia.Options.HuiaOptions options) =>
        {
            // Already at a tenant root (/{tenant}/ after base-path rebasing). Redirecting to /{tenant}/
            // again would loop, so hand off to the account UI instead: an anonymous caller to sign-in, an
            // authenticated one (e.g. just landed from a sign-in with no return URL, or an external
            // sign-up) to a plain confirmation page.
            if (context.Request.PathBase.HasValue)
            {
                if (context.RequestServices.GetService(typeof(HuiaUiMarker)) is null)
                {
                    return Http.Results.NoContent();
                }

                var page = context.User.Identity?.IsAuthenticated == true ? "signedin" : "login";

                // Carry a pending return URL (typically a /connect/authorize the client linked through
                // the tenant root) onto the account UI so the flow resumes once the user is signed in.
                var returnUrl = context.Request.Query["ReturnUrl"].ToString();
                var suffix = string.IsNullOrEmpty(returnUrl) ? string.Empty : $"?ReturnUrl={System.Uri.EscapeDataString(returnUrl)}";
                return Http.Results.Redirect($"{context.Request.PathBase}/identity/account/{page}{suffix}");
            }

            var tenant = tenantAccessor.CurrentTenantId()
                ?? fallbackTenantId
                ?? options.Tenants.Keys.FirstOrDefault();
            return tenant is null ? Http.Results.NotFound() : Http.Results.Redirect($"/{tenant}/");
        });

        return endpoints;
    }
}
