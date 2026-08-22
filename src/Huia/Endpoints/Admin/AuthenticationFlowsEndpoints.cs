using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Huia.Endpoints.Admin;

/// <summary>
/// Exposes which sign-in flows this app actually accepts (<c>huia.Authentication.Use...Flow()</c>), so the
/// admin UI's "New user" form only offers to create an account with an identifier the app can later sign
/// that account in with — e.g. hiding the phone-number option entirely when
/// <c>UsePasswordlessFlow</c> was never called. <see cref="UsersEndpoints.CreateAsync"/> enforces the same
/// rule server-side regardless of what this endpoint reports.
/// </summary>
internal static class AuthenticationFlowsEndpoints
{
    public static IEndpointRouteBuilder MapAuthenticationFlowsEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/authentication-flows", (HuiaOptions options) => Results.Ok(new AuthenticationFlowsResponse(
            options.Authentication.EmailAndPasswordFlowEnabled,
            options.Authentication.PasswordlessFlowEnabled)));

        return api;
    }

    internal sealed record AuthenticationFlowsResponse(bool EmailAndPasswordFlowEnabled, bool PasswordlessFlowEnabled);
}
