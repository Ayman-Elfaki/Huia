using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using OpenIddict.Abstractions;

namespace Huia.Applications;

/// <summary>
/// Resolves a safe, indefinitely-reusable destination for a specific client application — <see
/// cref="ClientApplicationOptions.HomeUri"/> if the app registered one, otherwise the origin of one of its
/// registered redirect_uris as a best-effort fallback. Used
/// wherever Huia needs to send the browser back to "the application" without a live OAuth request to resume:
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>An email confirmation/password reset link clicked out of band — see
/// <see cref="ResolveFromAuthorizeReturnUrlAsync"/>, used by <c>Register.cshtml.cs</c> and
/// <c>ResendEmailConfirmation.cshtml.cs</c>. The full <c>/connect/authorize?...</c> URL those would otherwise
/// bake in verbatim carries a one-time PKCE <c>code_challenge</c> tied to a <c>code_verifier</c> cookie in
/// whichever browser started the flow; replaying it once that flow has already completed — or from a
/// different browser/tab entirely, which an emailed link invites — fails the client's PKCE check instead of
/// signing the user in (e.g. Auth.js's "pkceCodeVerifier value could not be parsed").</item>
/// <item>Huia's own branded error page's "return home" link — see <see cref="ResolveFromClientIdAsync"/>,
/// used by <c>Error.cshtml.cs</c> for failures like an unknown <c>redirect_uri</c> where <c>client_id</c>
/// itself is still identifiable from the request.</item>
/// </list>
/// Landing back on the client application (rather than a dead PKCE replay, or Huia's own bare root) is as far
/// as Huia can safely take this on its own: only the client itself can start a new, correctly-signed OAuth
/// request (matching <c>state</c>/PKCE it generates and expects back) — Huia has no way to fabricate one
/// server-side that the client's own callback would accept.
/// </remarks>
internal static class ApplicationHomeUriResolver
{
    /// <summary>
    /// Returns a safe destination for the client identified by <paramref name="authorizeReturnUrl"/>'s
    /// <c>client_id</c>/<c>redirect_uri</c> parameters, if it's a genuine <c>/connect/authorize</c> URL naming
    /// a registered client with that exact redirect_uri — <see langword="null"/> otherwise (not a
    /// recognizable authorize URL, unknown client, an unrecognized redirect_uri, or no fallback available).
    /// </summary>
    public static async Task<string?> ResolveFromAuthorizeReturnUrlAsync(string? authorizeReturnUrl,
        IOpenIddictApplicationManager applicationManager, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(authorizeReturnUrl) ||
            !Uri.TryCreate(authorizeReturnUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var query = QueryHelpers.ParseQuery(uri.Query);
        if (!query.TryGetValue(OpenIddictConstants.Parameters.ClientId, out var clientId) ||
            !query.TryGetValue(OpenIddictConstants.Parameters.RedirectUri, out var redirectUri))
        {
            return null;
        }

        var application = await applicationManager.FindByClientIdAsync(clientId.ToString(), cancellationToken)
            .ConfigureAwait(false);
        if (application is null)
        {
            return null;
        }

        var registeredRedirectUris = await applicationManager.GetRedirectUrisAsync(application, cancellationToken)
            .ConfigureAwait(false);
        var redirectUriValue = redirectUri.ToString();

        return registeredRedirectUris.Contains(redirectUriValue, StringComparer.Ordinal)
            ? await ResolveForApplicationAsync(application, redirectUriValue, applicationManager, cancellationToken)
                .ConfigureAwait(false)
            : null;
    }

    /// <summary>
    /// Returns a safe destination for the client named by <paramref name="clientId"/> — <see langword="null"/>
    /// if it's missing, unknown, or has neither a <see cref="ClientApplicationOptions.HomeUri"/> nor any
    /// registered redirect_uri to fall back to.
    /// </summary>
    public static async Task<string?> ResolveFromClientIdAsync(string? clientId, IOpenIddictApplicationManager manager,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(clientId)) return null;

        var application = await manager.FindByClientIdAsync(clientId, ct).ConfigureAwait(false);
        if (application is null) return null;

        var redirectUris = await manager.GetRedirectUrisAsync(application, ct)
            .ConfigureAwait(false);

        return await ResolveForApplicationAsync(application, redirectUris.FirstOrDefault(), manager, ct)
            .ConfigureAwait(false);
    }

    private static async Task<string?> ResolveForApplicationAsync(object application, string? fallbackRedirectUri,
        IOpenIddictApplicationManager manager, CancellationToken ct)
    {
        var properties = await manager.GetPropertiesAsync(application, ct)
            .ConfigureAwait(false);

        if (properties.TryGetValue(HomeUriApplicationProperty.HomeUri, out var element) &&
            element.ValueKind == JsonValueKind.String)
        {
            return element.GetString();
        }

        return fallbackRedirectUri is not null &&
               Uri.TryCreate(fallbackRedirectUri, UriKind.Absolute, out var redirectUriParsed)
            ? redirectUriParsed.GetLeftPart(UriPartial.Authority)
            : null;
    }

    /// <summary>
    /// Whether <paramref name="origin"/> (a scheme+authority, no path) is a registered client's own
    /// <see cref="ClientApplicationOptions.HomeUri"/> or the origin of one of its registered redirect_uris.
    /// Used by <c>Huia.Helpers.ReturnUrlValidator</c> to accept the cross-origin returnUrl values this class
    /// itself hands out (e.g. to <c>ConfirmEmail.cshtml.cs</c>'s "Sign in" link) as safe final redirect
    /// targets, without opening up redirects to arbitrary third-party origins.
    /// </summary>
    public static async Task<bool> IsTrustedReturnOriginAsync(string origin,
        IOpenIddictApplicationManager applicationManager, CancellationToken cancellationToken = default)
    {
        await foreach (var application in applicationManager.ListAsync(cancellationToken: cancellationToken)
                           .ConfigureAwait(false))
        {
            var properties = await applicationManager.GetPropertiesAsync(application, cancellationToken)
                .ConfigureAwait(false);

            if (properties.TryGetValue(HomeUriApplicationProperty.HomeUri, out var element)
                && element.ValueKind == JsonValueKind.String
                && Uri.TryCreate(element.GetString(), UriKind.Absolute, out var homeUri)
                && string.Equals(homeUri.GetLeftPart(UriPartial.Authority), origin, StringComparison.Ordinal))
            {
                return true;
            }

            var redirectUris = await applicationManager.GetRedirectUrisAsync(application, cancellationToken)
                .ConfigureAwait(false);

            foreach (var redirectUri in redirectUris)
            {
                if (Uri.TryCreate(redirectUri, UriKind.Absolute, out var parsed)
                    && string.Equals(parsed.GetLeftPart(UriPartial.Authority), origin, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }
}