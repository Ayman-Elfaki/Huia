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
    /// scope was granted. <c>sid</c> is the one exception — per the OpenID Connect Front-/Back-Channel
    /// Logout 1.0 specs it's an identity-token/logout_token concept a relying party needs to correlate a
    /// logout notification against its own session state, so it's unconditional (no scope gate) and,
    /// unlike every other claim here, does NOT go into the access token — nothing reads it there today, and
    /// keeping it identity-token-only matches the specs' own framing.
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
                yield return OpenIddictConstants.Destinations.AccessToken;
                yield return OpenIddictConstants.Destinations.IdentityToken;
                break;
            case SessionClaimTypes.Sid:
                yield return OpenIddictConstants.Destinations.IdentityToken;
                break;
            case "AspNet.Identity.SecurityStamp": yield break;
            default:
                yield return OpenIddictConstants.Destinations.AccessToken;
                break;
        }
    }
}