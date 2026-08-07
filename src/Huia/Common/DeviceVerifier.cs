using System.Collections.Immutable;
using System.Globalization;
using System.Security.Claims;
using Huia.Endpoints.Connect;
using Huia.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Server.AspNetCore.OpenIddictServerAspNetCoreConstants;


namespace Huia.Common;

/// <summary>
/// Shared logic for approving/denying a pending device code, used by both the JSON <c>/connect/verify</c>
/// endpoints (<see cref="DeviceEndpoints"/>) and the bundled <c>Device</c> Razor Page under
/// <c>Areas/Identity/Pages/Account</c> — the latter is the endpoint URI actually reported as
/// <c>verification_uri</c> by default (see <c>ServiceCollectionExtensions</c>'
/// <c>SetEndUserVerificationEndpointUris</c> ordering: the first configured URI is the one OpenIddict
/// advertises); <c>/connect/verify</c> stays mapped as a secondary URI for a client that wants the raw JSON
/// contract instead of a browser UI.
/// </summary>
internal static class DeviceVerifier
{
    /// <summary>
    /// Authenticates the device code tied to whatever <c>user_code</c> the current request carries — a query
    /// parameter on <c>GET</c>, a form field on <c>POST</c> — against OpenIddict's own end-user-verification
    /// handling for whichever configured URI the request matched. Distinct from the browser's own Identity
    /// cookie principal (<see cref="HttpContext.User"/>): this one describes the *device*, not whoever's
    /// approving it.
    /// </summary>
    public static Task<AuthenticateResult> AuthenticateAsync(HttpContext httpContext)
        => httpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

    /// <summary>
    /// Resolves <paramref name="result"/> into the application/scopes to show the approving user, or
    /// <see langword="null"/> if the code wasn't valid (unknown, expired, or already used).
    /// </summary>
    public static async Task<DeviceVerificationRequest?> TryDescribeAsync(AuthenticateResult result,
        IOpenIddictApplicationManager applicationManager)
    {
        var clientId = result is { Succeeded: true }
            ? result.Principal!.GetClaim(OpenIddictConstants.Claims.ClientId)
            : null;
        if (string.IsNullOrEmpty(clientId))
        {
            return null;
        }

        var application = await applicationManager.FindByClientIdAsync(clientId).ConfigureAwait(false)
                          ?? throw new InvalidOperationException(
                              "Details concerning the calling client application cannot be found.");

        return new DeviceVerificationRequest(
            await applicationManager.GetLocalizedDisplayNameAsync(application, CultureInfo.CurrentUICulture)
                .ConfigureAwait(false),
            result.Principal!.GetScopes(),
            result.Properties?.GetTokenValue(Tokens.UserCode));
    }

    /// <summary>
    /// Builds the identity for a just-approved device code, ready to be signed in against
    /// <see cref="OpenIddictServerAspNetCoreDefaults.AuthenticationScheme"/> (a plain
    /// <see cref="ClaimsPrincipal"/> here rather than an <c>IResult</c>/<c>SignInAsync</c> call, so it works
    /// equally from a minimal API handler or a Razor Page).
    /// </summary>
    public static async Task<ClaimsIdentity> ApproveAsync(HuiaUser user, ImmutableArray<string> scopes,
        UserManager<HuiaUser> userManager, IOpenIddictScopeManager scopeManager)
    {
        var identity = await ClaimsUtils.CreateUserIdentityAsync(userManager, user, scopes)
            .ConfigureAwait(false);
        // Without this, TokenEndpoints.HandleReAuthenticateAsync's own identity.SetResources(principal.GetResources())
        // — which just carries forward whatever this sign-in attached — would always find nothing: unlike
        // AuthorizationEndpoints' equivalent approval step, this identity would otherwise start out with no
        // resources of its own. A device-flow token would then never carry an aud claim no matter which
        // scopes were requested, failing any resource server's HuiaServerOptions.RequireAudiences check outright.
        identity.SetResources(await scopeManager.ListResourcesAsync(scopes).ToListAsync().ConfigureAwait(false));
        return identity;
    }
}

/// <summary>A pending device code's application/scope, ready to show the user deciding whether to approve it.</summary>
// Kept alongside its only producer, DeviceVerifier.TryDescribeAsync, rather than its own file.
#pragma warning disable MA0048
internal sealed record DeviceVerificationRequest(
    string? ApplicationName,
    ImmutableArray<string> Scopes,
    string? UserCode);