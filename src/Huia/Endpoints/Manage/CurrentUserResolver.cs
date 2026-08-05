using System.Security.Claims;
using Huia.Identity;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;

namespace Huia.Endpoints.Manage;

/// <summary>
/// Resolves the signed-in <see cref="HuiaUser"/> from a <see cref="ClaimsPrincipal"/> that may have
/// authenticated via either the <c>Identity.Application</c> cookie or a bearer access token validated by
/// OpenIddict (see <c>MapHuiaManageEndpoints</c>, which accepts both). <see cref="UserManager{TUser}.GetUserAsync"/>
/// alone only covers the cookie case: it looks up <see cref="ClaimTypes.NameIdentifier"/>, which ASP.NET
/// Identity's own cookie principal carries but an OpenIddict-issued bearer principal doesn't — OpenIddict uses
/// the raw <c>sub</c> claim instead of the legacy <c>ClaimTypes</c> URIs (the same split
/// <c>UserinfoEndpoints</c> already handles for its own bearer-only endpoint).
/// </summary>
internal static class CurrentUserResolver
{
    public static Task<HuiaUser?> GetSignedInUserAsync(this UserManager<HuiaUser> userManager,
        ClaimsPrincipal principal)
    {
        var userId = principal.FindFirstValue(OpenIddictConstants.Claims.Subject)
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

        return userId is null ? Task.FromResult<HuiaUser?>(null) : userManager.FindByIdAsync(userId);
    }
}
