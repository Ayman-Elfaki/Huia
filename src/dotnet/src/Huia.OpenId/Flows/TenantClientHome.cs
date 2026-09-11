using Huia.OpenId.Options;
using Huia.Options;

namespace Huia.OpenId.Flows;

/// <summary>
/// Resolves the URL to send an interactive user to when there is no OAuth flow in progress — after a
/// sign-in that carried no <c>returnUrl</c>, or after an interactive sign-out. The identity provider's
/// own tenant root has nothing to serve (it just bounces to sign-in), so prefer the first registered
/// client application's home / post-logout URL for the tenant.
/// </summary>
public static class TenantClientHome
{
    /// <summary>The first absolute home / post-logout URI across the tenant's registered clients, if any.</summary>
    /// <param name="tenant">The resolved tenant options, or <see langword="null"/>.</param>
    /// <returns>An absolute URL, or <see langword="null"/> when the tenant has no client with one.</returns>
    public static string? Resolve(TenantOptions? tenant) =>
        tenant?.GetHuiaOpenId()?.Clients
            .SelectMany(client => client.HomeUris.Concat(client.PostLogoutRedirectUris))
            .FirstOrDefault(uri => uri.IsAbsoluteUri)?
            .ToString();
}
