using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using OpenIddict.Client.AspNetCore;

namespace Huia.Endpoints;

/// <summary>
/// Bridges OpenIddict's client redirection endpoint back into ASP.NET Core Identity's external-login
/// mechanism. Unlike the ASP.NET Core remote-authentication handlers (<c>AddGoogle</c>, etc.) Huia's external
/// login used to be built on, OpenIddict's client stack completes the OAuth2/OIDC handshake itself but never
/// signs the result into any cookie —
/// <c>httpContext.AuthenticateAsync(OpenIddictClientAspNetCoreDefaults.AuthenticationScheme)</c> is how the
/// app retrieves it. This endpoint does exactly that, then signs the resulting identity into
/// <see cref="IdentityConstants.ExternalScheme"/> — the same cookie a remote-authentication handler used to
/// populate directly — so <c>ExternalLoginModel</c> and <c>ManageExternalLoginsEndpoints</c>'s existing
/// <c>SignInManager&lt;HuiaUser&gt;</c>-based logic (sign-in, 2FA/lockout routing, auto-provisioning,
/// email-collision handling, account linking — shared by both the sign-in flow and the "link a provider to my
/// signed-in account" flow, distinguished only by which one set <c>AuthenticationProperties.RedirectUri</c> at
/// challenge time) keeps working completely unmodified.
/// </summary>
/// <remarks>
/// Every provider registered via <c>ext.WebProviders</c>/<c>ext.Client</c> (see
/// <see cref="Huia.Authentication.ExternalAuthenticationFlowBuilder"/>) must set its own
/// <c>RedirectUri</c>/<c>SetRedirectUri(...)</c> to <c>callback/login/{a unique provider name}</c> — the
/// route this endpoint is mapped at — instead of the old ASP.NET Core <c>CallbackPath</c> convention. See
/// docs/external-providers.md.
/// </remarks>
internal static class ExternalLoginCallbackEndpoints
{
    private const string DefaultErrorRedirect = "/identity/account/login";

    public static RouteGroupBuilder MapExternalLoginCallbackEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("callback/login");

        // OpenIddict's redirection endpoint accepts whichever HTTP method the provider's authorization
        // response used — a GET for the common "query" response mode, a POST for "form_post" (some
        // OIDC providers, and any generic registration that opts into it).
        group.MapGet("{provider}", CallbackAsync);
        group.MapPost("{provider}", CallbackAsync);

        return group;
    }

    private static async Task<IResult> CallbackAsync(HttpContext httpContext, ILoggerFactory loggerFactory)
    {
        var result = await httpContext.AuthenticateAsync(OpenIddictClientAspNetCoreDefaults.AuthenticationScheme)
            .ConfigureAwait(false);
        
        if (!result.Succeeded || result.Principal is null)
        {
            loggerFactory.CreateLogger("Huia.ExternalLoginCallback")
                .LogWarning(result.Failure, "External sign-in callback failed.");

            var errorReturnUrl = result.Properties?.RedirectUri ?? DefaultErrorRedirect;
            return Results.Redirect(QueryHelpers.AddQueryString(errorReturnUrl, "remoteError",
                result.Failure?.Message ?? "unknown_error"));
        }

        // SignInManager<HuiaUser>.GetExternalLoginInfoAsync() (called downstream by ExternalLoginModel /
        // ManageExternalLoginsEndpoints) hardcodes ClaimTypes.NameIdentifier as the external identity's
        // provider key — OpenIddict's merged principal carries the subject under the raw OIDC "sub" claim
        // instead, since it never maps onto ClaimTypes the way AddGoogle/AddMicrosoftAccount used to.
        var identity = new ClaimsIdentity(result.Principal.Identity as ClaimsIdentity);
        if (identity.FindFirst(ClaimTypes.NameIdentifier) is null &&
            result.Principal.FindFirstValue(OpenIddictConstants.Claims.Subject) is { } subject)
        {
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, subject));
        }

        await httpContext.SignInAsync(IdentityConstants.ExternalScheme, new ClaimsPrincipal(identity),
            result.Properties).ConfigureAwait(false);

        // result.Properties.RedirectUri is whichever caller started the challenge — ExternalLoginModel's own
        // callback page for a sign-in attempt, or ManageExternalLoginsEndpoints' link-callback for linking a
        // provider to an already-signed-in account — set via
        // SignInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl[, userId]).
        return Results.Redirect(result.Properties?.RedirectUri ?? DefaultErrorRedirect);
    }
}
