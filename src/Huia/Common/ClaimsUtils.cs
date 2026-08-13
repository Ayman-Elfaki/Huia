using System.Security.Claims;
using Huia.Identity;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;

namespace Huia.Common;

internal static class ClaimsUtils
{
    public static async Task<ClaimsIdentity> CreateUserIdentityAsync(UserManager<HuiaUser> userManager, HuiaUser user,
        IEnumerable<string> scopes)
    {
        var identity = new ClaimsIdentity(authenticationType: "Huia", nameType: OpenIddictConstants.Claims.Name,
            roleType: OpenIddictConstants.Claims.Role);

        identity.SetClaim(OpenIddictConstants.Claims.Subject, await userManager.GetUserIdAsync(user).ConfigureAwait(false))
            .SetClaim(OpenIddictConstants.Claims.Email, await userManager.GetEmailAsync(user).ConfigureAwait(false))
            .SetClaim(OpenIddictConstants.Claims.Name, await userManager.GetUserNameAsync(user).ConfigureAwait(false))
            .SetClaim(OpenIddictConstants.Claims.PreferredUsername, await userManager.GetUserNameAsync(user).ConfigureAwait(false))
            .SetClaim(OpenIddictConstants.Claims.GivenName, user.FirstName)
            .SetClaim(OpenIddictConstants.Claims.FamilyName, user.LastName)
            .SetClaim(OpenIddictConstants.Claims.Picture, user.Picture)
            .SetClaims(OpenIddictConstants.Claims.Role, [.. await userManager.GetRolesAsync(user).ConfigureAwait(false)]);

        identity.SetScopes(scopes);
        identity.SetDestinations(claim => GetDestinations(claim, identity));

        return identity;
    }

    /// <summary>
    /// Standard OpenIddict claim-destination rule: claims always go into the access token; the standard
    /// OIDC profile/email/role claims are mirrored into the identity token only when the corresponding
    /// scope was granted.
    /// </summary>
    public static IEnumerable<string> GetDestinations(Claim claim, ClaimsIdentity identity)
    {
        switch (claim.Type)
        {
            case OpenIddictConstants.Claims.Name or OpenIddictConstants.Claims.PreferredUsername
                or OpenIddictConstants.Claims.GivenName or OpenIddictConstants.Claims.FamilyName
                or OpenIddictConstants.Claims.Picture
                when identity.HasScope(OpenIddictConstants.Scopes.Profile):
            case OpenIddictConstants.Claims.Email when identity.HasScope(OpenIddictConstants.Scopes.Email):
            case OpenIddictConstants.Claims.Role when identity.HasScope(OpenIddictConstants.Scopes.Roles):
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