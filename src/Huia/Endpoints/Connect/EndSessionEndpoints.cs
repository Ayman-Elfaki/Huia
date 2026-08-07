using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using OpenIddict.Server.AspNetCore;

namespace Huia.Endpoints.Connect;

internal static class EndSessionEndpoints
{
    public static IEndpointRouteBuilder MapEndSessionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapMethods("logout", [HttpMethods.Get, HttpMethods.Post], (Delegate)HandleAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleAsync(HttpContext httpContext)
    {
        await httpContext.SignOutAsync(IdentityConstants.ApplicationScheme).ConfigureAwait(false);

        // The identity cookie is already gone, so this is safe to let OpenIddict finish immediately — it
        // performs its own (already-validated) post_logout_redirect_uri/state redirect back to the client
        // that initiated this request.
        return Results.SignOut(authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
    }
}