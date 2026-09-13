using Huia.Entities;
using Huia.Headless.Endpoints;
using Microsoft.AspNetCore.Builder;

namespace Microsoft.AspNetCore.Routing;

/// <summary>Endpoint mapping for the single-tenant Headless flavor of Huia.</summary>
public static class HuiaHeadlessEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps ASP.NET Core Identity's own API endpoints (register, login, refresh, email confirmation,
    /// password reset, 2FA, <c>/manage/info</c>) under <c>identity/</c>, plus <c>identity/me</c> (richer
    /// claims for session population) and the Huia passkey and passwordless-SMS phone-login endpoints.
    /// <c>identity/register</c> is Huia's own (requires and persists first/last name) — see
    /// <see cref="RegisterEndpoints"/> — and takes precedence over the stock one <c>MapIdentityApi</c>
    /// still maps alongside it.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The endpoint route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapHuiaHeadlessEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapHuiaHeadlessRegisterEndpoint();
        endpoints.MapGroup("identity").MapIdentityApi<HuiaUser>();
        endpoints.MapHuiaHeadlessMeEndpoints();
        endpoints.MapHuiaHeadlessPasskeyEndpoints();
        endpoints.MapHuiaHeadlessPhoneEndpoints();
        endpoints.MapHuiaHeadlessExternalEndpoints();

        return endpoints;
    }

    /// <summary>
    /// Maps the Admin endpoints (<c>/admin/*</c>) for Huia Headless and returns the group
    /// <em>without</em> an authorization policy. The host attaches its own policy via
    /// <c>.RequireAuthorization(...)</c>.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The admin route group builder, for chaining authorization.</returns>
    public static RouteGroupBuilder MapHuiaHeadlessAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return AdminEndpoints.MapHuiaHeadlessAdminEndpointsGroup(endpoints);
    }

}

