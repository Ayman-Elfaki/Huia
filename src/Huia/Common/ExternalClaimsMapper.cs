using System.Security.Claims;
using Microsoft.AspNetCore.Identity;

namespace Huia.Common;

/// <summary>
/// Reads the profile claims an external sign-in provider (<see cref="ExternalLoginInfo.Principal"/>) supplied,
/// for pre-filling/auto-provisioning a new <c>HuiaUser</c>. Falls back to the raw OIDC claim names alongside
/// the standard <see cref="ClaimTypes"/> ones — OpenIddict's client stack (the external-login mechanism
/// itself, not just a specific provider) never maps claims onto <see cref="ClaimTypes"/> the way the old
/// ASP.NET Core <c>AddGoogle</c>/<c>AddMicrosoftAccount</c> handlers used to, so every provider's claims are
/// read by their raw OIDC names here.
/// </summary>
internal static class ExternalClaimsMapper
{
    public static string? GetEmail(ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.Email) ?? principal.FindFirstValue("email");

    public static string? GetFirstName(ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.GivenName) ?? principal.FindFirstValue("given_name");

    public static string? GetLastName(ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.Surname) ?? principal.FindFirstValue("family_name");

    /// <summary>A URL to the user's avatar/profile picture, if the provider supplied one — Google and
    /// Microsoft both send this as the raw OIDC <c>picture</c> claim (there's no <see cref="ClaimTypes"/>
    /// equivalent).</summary>
    public static string? GetPicture(ClaimsPrincipal principal) => principal.FindFirstValue("picture");

    /// <summary>
    /// Whether the provider already verified this identity's email — Google reliably sends
    /// <c>email_verified</c>; a generic provider that doesn't is treated as unverified, falling back to
    /// Huia's normal confirmation-email flow.
    /// </summary>
    public static bool IsEmailVerified(ClaimsPrincipal principal) =>
        string.Equals(principal.FindFirstValue("email_verified"), "true", StringComparison.OrdinalIgnoreCase);
}
