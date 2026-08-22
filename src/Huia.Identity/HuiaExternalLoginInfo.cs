using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace Huia.Identity;

/// <summary>
/// An external (third-party) identity mid-sign-in, resolved from the <see cref="HuiaAuthenticationDefaults.ExternalScheme"/>
/// cookie by <see cref="HuiaSignInManager.GetExternalLoginInfoAsync(string?)"/>. Replaces ASP.NET Core
/// Identity's <c>ExternalLoginInfo</c>.
/// </summary>
public sealed class HuiaExternalLoginInfo
{
    /// <summary>The provider's logical name (e.g. <c>"Google"</c>).</summary>
    public required string LoginProvider { get; init; }

    /// <summary>The user's unique id at the provider.</summary>
    public required string ProviderKey { get; init; }

    /// <summary>The provider's display name, for UI.</summary>
    public string? ProviderDisplayName { get; init; }

    /// <summary>The full claims principal the provider returned.</summary>
    public required ClaimsPrincipal Principal { get; init; }

    /// <summary>The authentication properties carried through the external sign-in round trip — e.g. access/
    /// refresh tokens from the provider, via <see cref="AuthenticationProperties.GetTokens"/>.</summary>
    public AuthenticationProperties? AuthenticationProperties { get; init; }
}
