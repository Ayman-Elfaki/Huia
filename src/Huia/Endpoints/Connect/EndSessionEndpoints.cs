using System.Security.Claims;
using Huia.Common;
using Huia.Core;
using Huia.Emails;
using Huia.Identity;
using Huia.Sessions;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;

namespace Huia.Endpoints.Connect;

internal static class EndSessionEndpoints
{
    public static IEndpointRouteBuilder MapEndSessionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapMethods("logout", [HttpMethods.Get, HttpMethods.Post], (Delegate)HandleAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        UserManager<HuiaUser> userManager,
        LogoutNotifier logoutNotifier,
        UserSessionService sessions,
        HuiaOptions huiaOptions,
        RazorViewRenderer viewRenderer,
        ILogger<LogoutNotifier> logger,
        CancellationToken cancellationToken)
    {
        var request = httpContext.GetOpenIddictServerRequest()
                      ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        var result = await httpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var user = result is { Succeeded: true } ? await userManager.GetUserAsync(result.Principal) : null;
        var sessionId = result is { Succeeded: true } ? result.Principal!.FindFirstValue(SessionClaimTypes.Sid) : null;

        await httpContext.SignOutAsync(IdentityConstants.ApplicationScheme);

        if (user is not null && sessionId is null)
        {
            // A cookie that predates this feature — no sid at all, so there's no session to end/notify
            // anyone about. Self-resolving: the Identity.Application cookie is short-lived (10 minutes,
            // non-sliding — see ServiceCollectionExtensions), so this gap closes on its own shortly after
            // upgrade rather than needing a fallback to the old subject-wide notification behavior.
            logger.LogInformation(
                "User {UserId} signed out via a pre-upgrade cookie with no session id; no relying parties were notified.",
                await userManager.GetUserIdAsync(user).ConfigureAwait(false));
        }

        if (user is not null && sessionId is not null)
        {
            var subject = await userManager.GetUserIdAsync(user);
            var targets = await logoutNotifier.CollectAsync(subject, sessionId, request.ClientId, cancellationToken);

            // Back-channel logout is invisible to the browser, so it can happen inline here; front-channel
            // logout needs the browser to actually load each relying party's iframe, which requires handing
            // it an HTML page instead of the redirect OpenIddict would otherwise send straight back to the
            // client that initiated this request.
            await logoutNotifier.NotifyBackChannelAsync(sessionId, targets.BackChannelLogoutTargets, cancellationToken);
            await sessions.RevokeAsync(sessionId, cancellationToken);

            if (targets.FrontChannelLogoutUris.Count > 0)
            {
                var continueUrl = BuildContinueUrl(request);
                // Normalized the same way OpenIddict's own SetIssuer(new Uri(...)) does (which, for a bare
                // authority with no path, adds a trailing slash) — this must match byte-for-byte what
                // relying parties already know as this OP's issuer from discovery/their ID tokens' iss claim.
                var issuer = new Uri(huiaOptions.Issuer, UriKind.Absolute).AbsoluteUri;
                var iframeSources = BuildIframeSources(targets.FrontChannelLogoutUris, issuer);
                var html = await viewRenderer.RenderAsync("/Areas/Identity/Pages/Account/FrontChannelSignOut.cshtml",
                    new FrontChannelSignOutViewModel(iframeSources, continueUrl));
                return Results.Content(html, "text/html");
            }
        }

        // Either there was no session to notify anyone about, or nobody needed a front-channel iframe — the
        // identity cookie is already gone, so this is safe to let OpenIddict finish immediately. It also
        // handles the case where BuildFrontChannelLogoutPage's continuation script re-requests this same URL:
        // by then the cookie is gone, user is null, and this branch runs straight away.
        return Results.SignOut(authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
    }

    /// <summary>
    /// Re-invokes <c>/connect/logout</c> with the same end-session parameters once the front-channel logout
    /// iframes below have had a chance to load, so OpenIddict can perform its own (already-validated)
    /// post_logout_redirect_uri/state redirect exactly as it would have without this intermediate page.
    /// </summary>
    private static string BuildContinueUrl(OpenIddictRequest request)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["client_id"] = request.ClientId,
            ["post_logout_redirect_uri"] = request.PostLogoutRedirectUri,
            ["state"] = request.State,
            ["id_token_hint"] = request.IdTokenHint,
        };

        return QueryHelpers.AddQueryString("/connect/logout",
            parameters.Where(pair => pair.Value is not null));
    }

    /// <summary>
    /// Builds each relying party's front-channel logout iframe <c>src</c> — the logout URI plus the required
    /// <c>iss</c> query parameter (see the OpenID Connect RP-Initiated Logout spec). Left un-encoded: Razor's
    /// <c>@@src</c> interpolation in FrontChannelSignOut.cshtml HTML-encodes it on write.
    /// </summary>
    private static IReadOnlyList<string> BuildIframeSources(IReadOnlyList<Uri> frontChannelLogoutUris, string issuer)
        => [.. frontChannelLogoutUris.Select(uri =>
        {
            var separator = uri.Query.Length > 0 ? '&' : '?';
            return $"{uri}{separator}iss={Uri.EscapeDataString(issuer)}";
        })];
}