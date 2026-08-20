using System.Security.Claims;
using Huia.Common;
using Huia.Identity;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Routing;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;

namespace Huia.Endpoints.Connect;

internal static class AuthorizationEndpoints
{
    public static IEndpointRouteBuilder MapAuthorizationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapMethods("authorize", [HttpMethods.Get, HttpMethods.Post], HandleAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        UserManager<HuiaUser> userManager,
        IOpenIddictScopeManager scopeManager,
        IOpenIddictApplicationManager applicationManager,
        HuiaOptions huiaOptions)
    {
        var request = httpContext.GetOpenIddictServerRequest()
                      ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        ApplyUiLocales(httpContext, request, huiaOptions);

        var result = await httpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme).ConfigureAwait(false);
        var user = result is { Succeeded: true } ? await userManager.GetUserAsync(result.Principal).ConfigureAwait(false) : null;

        if (user is null)
        {
            // Not signed in, or the cookie names a user that no longer exists (e.g. a stale cookie left over
            // from a wiped/reseeded database) — either way, treat it as "not signed in" rather than 500ing.
            if (result is { Succeeded: true })
            {
                await httpContext.SignOutAsync(IdentityConstants.ApplicationScheme).ConfigureAwait(false);
            }

            var returnUrl = Uri.EscapeDataString(httpContext.Request.GetEncodedUrl());
            var separator = huiaOptions.LoginPath.Contains('?') ? '&' : '?';
            return Results.Redirect($"{huiaOptions.LoginPath}{separator}ReturnUrl={returnUrl}");
        }

        var scopes = request.GetScopes();
        var clientId = request.ClientId;

        if (!scopes.Any() && clientId is not null)
        {
            // RFC 6749 §3.3: if the client omits `scope` entirely, fall back to every scope it's allowed to
            // request rather than silently issuing a token with none. Some OAuth testing tools (e.g.
            // Scalar's own "Authorize" UI) don't reliably include a scope parameter even when scopes appear
            // selected in their panel, which would otherwise produce a token no protected endpoint accepts.
            var application = await applicationManager.FindByClientIdAsync(clientId).ConfigureAwait(false);
            if (application is not null)
            {
                var permissions = await applicationManager.GetPermissionsAsync(application).ConfigureAwait(false);
                scopes =
                [
                    .. permissions
                        .Where(permission => permission.StartsWith(OpenIddictConstants.Permissions.Prefixes.Scope,
                            StringComparison.Ordinal))
                        .Select(permission => permission[OpenIddictConstants.Permissions.Prefixes.Scope.Length..])
                ];
            }
        }

        var identity = await ClaimsUtils.CreateUserIdentityAsync(userManager, user, scopes).ConfigureAwait(false);
        identity.SetResources(await scopeManager.ListResourcesAsync(scopes).ToListAsync().ConfigureAwait(false));

        // NOTE: Huia auto-approves consent for every client for v1 (no consent-screen UI is implemented
        // yet). Third-party clients seeded with an explicit ConsentType are still issued tokens; treat this
        // as a known limitation rather than a real consent decision.
        return Results.SignIn(new ClaimsPrincipal(identity), properties: null,
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Honors the OpenID Connect <c>ui_locales</c> parameter (a space-separated, preference-ordered list of
    /// BCP 47 language tags — see the OIDC Core spec §3.1.2.1) by persisting the first one Huia actually
    /// supports as the request-localization culture cookie. This is what lets a client's own chosen language
    /// (e.g. a locale-aware frontend redirecting here for sign-in) carry over to Huia's server-rendered
    /// Identity UI pages, rather than those pages falling back to the browser's Accept-Language header —
    /// which reflects the user's OS/browser settings, not necessarily whatever language they were just using
    /// in the client. The cookie (not a query string) is what survives the whole login → register →
    /// redirect-back-to-authorize round trip without every page/link needing to thread a query parameter
    /// through by hand.
    /// </summary>
    private static void ApplyUiLocales(HttpContext httpContext, OpenIddictRequest request,
        HuiaOptions huiaOptions)
    {
        if (string.IsNullOrEmpty(request.UiLocales)) return;

        var supportedCultures = huiaOptions.Localization.Cultures;
        var requestedTags = request.UiLocales.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var match = requestedTags
            .Select(tag =>
                supportedCultures.FirstOrDefault(culture =>
                    culture.Name.Equals(tag, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(culture => culture is not null);

        if (match is not null)
        {
            httpContext.Response.Cookies.Append(
                CookieRequestCultureProvider.DefaultCookieName,
                CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(match)));
        }
    }
}