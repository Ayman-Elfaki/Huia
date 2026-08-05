using System.Security.Claims;
using Huia.Identity;
using Huia.Sessions;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;

namespace Huia.Common;

internal static class ClaimsUtils
{
    public static async Task<ClaimsIdentity> CreateUserIdentityAsync(UserManager<HuiaUser> userManager, HuiaUser user,
        IEnumerable<string> scopes, string? sessionId = null)
    {
        var identity = new ClaimsIdentity(authenticationType: "Huia", nameType: OpenIddictConstants.Claims.Name,
            roleType: OpenIddictConstants.Claims.Role);

        identity.SetClaim(OpenIddictConstants.Claims.Subject, await userManager.GetUserIdAsync(user))
            .SetClaim(OpenIddictConstants.Claims.Email, await userManager.GetEmailAsync(user))
            .SetClaim(OpenIddictConstants.Claims.Name, await userManager.GetUserNameAsync(user))
            .SetClaim(OpenIddictConstants.Claims.PreferredUsername, await userManager.GetUserNameAsync(user))
            .SetClaim(OpenIddictConstants.Claims.GivenName, user.FirstName)
            .SetClaim(OpenIddictConstants.Claims.FamilyName, user.LastName)
            .SetClaims(OpenIddictConstants.Claims.Role, [.. await userManager.GetRolesAsync(user)]);

        // sessionId is null for grants with no user session behind them at all (see TokenEndpoints'
        // client_credentials handling, which never calls this method) — every interactive/user-bound path
        // (AuthorizationEndpoints, TokenEndpoints' authorization_code/refresh_token/device_code handling,
        // DeviceEndpoints) always has one by the time it gets here.
        if (sessionId is not null)
        {
            identity.SetClaim(SessionClaimTypes.Sid, sessionId);
        }

        identity.SetScopes(scopes);
        identity.SetDestinations(claim => GetDestinations(claim, identity));

        return identity;
    }

    /// <summary>
    /// Standard OpenIddict claim-destination rule: claims always go into the access token; the standard
    /// OIDC profile/email/role claims are mirrored into the identity token only when the corresponding
    /// scope was granted. <c>sid</c> is unconditional (no scope gate) per the OpenID Connect
    /// Front-/Back-Channel Logout 1.0 specs — a relying party needs it in the identity token/logout_token to
    /// correlate a logout notification against its own session state — and also goes into the access token:
    /// <c>Endpoints.Manage.ManageSessionsEndpoints</c> reads it off the bearer-authenticated principal to
    /// mark which of the caller's own sessions is the one making the request.
    /// </summary>
    public static IEnumerable<string> GetDestinations(Claim claim, ClaimsIdentity identity)
    {
        switch (claim.Type)
        {
            case OpenIddictConstants.Claims.Name or OpenIddictConstants.Claims.PreferredUsername
                or OpenIddictConstants.Claims.GivenName or OpenIddictConstants.Claims.FamilyName
                when identity.HasScope(OpenIddictConstants.Scopes.Profile):
            case OpenIddictConstants.Claims.Email when identity.HasScope(OpenIddictConstants.Scopes.Email):
            case OpenIddictConstants.Claims.Role when identity.HasScope(OpenIddictConstants.Scopes.Roles):
            case SessionClaimTypes.Sid:
                yield return OpenIddictConstants.Destinations.AccessToken;
                yield return OpenIddictConstants.Destinations.IdentityToken;
                break;
            case "AspNet.Identity.SecurityStamp": yield break;
            default:
                yield return OpenIddictConstants.Destinations.AccessToken;
                break;
        }
    }
}