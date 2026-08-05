using System.Text.Json;
using Huia.Applications;
using Huia.Core;
using Huia.Sessions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using OpenIddict.Abstractions;
using OpenIddict.Server;

namespace Huia.Common;

/// <summary>
/// Notifies other OpenID Connect relying parties that one of a user's sessions at Huia has ended — the
/// piece <c>/connect/logout</c> (RP-initiated logout) only handles for the single client that sent the user
/// there. For every other client with a live authorization tied to that <em>same session</em>, this
/// front-channel-logs-out (an iframe onto its <see cref="ClientApplicationOptions.FrontChannelLogoutUri"/>,
/// if registered) and/or back-channel-logs-out (a direct server-to-server POST of a signed
/// <c>logout_token</c> to its <see cref="ClientApplicationOptions.BackChannelLogoutUri"/>, if registered)
/// per the OpenID Connect Front-Channel/Back-Channel Logout 1.0 specs.
/// </summary>
/// <remarks>
/// Session-scoped, not subject-wide: signing out of one session (one browser, one login) leaves the user's
/// other sessions — a different browser, a different device — untouched and unnotified. That's what the
/// <c>sid</c> claim is for; see <c>Huia.Sessions.AttachSessionAuthorizationHandler</c>, which is what makes
/// an authorization discoverable by session in the first place, and the admin <c>/sessions/revoke-all</c>
/// endpoint for an explicit "sign out everywhere" action, which this notifier alone doesn't provide.
/// </remarks>
public sealed class LogoutNotifier(
    IOpenIddictAuthorizationManager authorizationManager,
    IOpenIddictApplicationManager applicationManager,
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<OpenIddictServerOptions> serverOptions,
    HuiaOptions huiaOptions,
    ILogger<LogoutNotifier> logger)
{
    /// <summary>The named <see cref="HttpClient"/> back-channel logout POSTs are sent through.</summary>
    public const string BackChannelHttpClientName = "Huia.BackChannelLogout";

    private static readonly TimeSpan BackChannelTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Finds every other client with a live (non-revoked) authorization tied to <paramref name="sessionId"/>
    /// — excluding <paramref name="excludeClientId"/>, the client that itself just initiated the logout —
    /// and splits them into front-channel and back-channel logout targets based on what each registered.
    /// </summary>
    public async Task<LogoutTargets> CollectAsync(string subject, string sessionId, string? excludeClientId,
        CancellationToken cancellationToken)
    {
        List<Uri>? frontChannel = null;
        List<(string ClientId, Uri Uri)>? backChannel = null;
        var seenApplicationIds = new HashSet<string>(StringComparer.Ordinal);

        await foreach (var authorization in SessionAuthorizationLookup
                           .FindBySessionAsync(authorizationManager, subject, sessionId, cancellationToken)
                           .ConfigureAwait(false))
        {
            if (await authorizationManager.GetStatusAsync(authorization, cancellationToken).ConfigureAwait(false)
                != OpenIddictConstants.Statuses.Valid)
            {
                continue;
            }

            var applicationId = await authorizationManager.GetApplicationIdAsync(authorization, cancellationToken)
                .ConfigureAwait(false);
            if (applicationId is null || !seenApplicationIds.Add(applicationId))
            {
                continue;
            }

            var application = await applicationManager.FindByIdAsync(applicationId, cancellationToken)
                .ConfigureAwait(false);
            if (application is null)
            {
                continue;
            }

            var clientId = await applicationManager.GetClientIdAsync(application, cancellationToken)
                .ConfigureAwait(false);
            if (clientId is null || clientId == excludeClientId)
            {
                continue;
            }

            var properties = await applicationManager.GetPropertiesAsync(application, cancellationToken)
                .ConfigureAwait(false);

            if (TryGetUri(properties, LogoutApplicationProperties.FrontChannelLogoutUri, out var frontUri))
            {
                (frontChannel ??= []).Add(frontUri);
            }

            if (TryGetUri(properties, LogoutApplicationProperties.BackChannelLogoutUri, out var backUri))
            {
                (backChannel ??= []).Add((clientId, backUri));
            }
        }

        return new LogoutTargets(
            (IReadOnlyList<Uri>?)frontChannel ?? [],
            (IReadOnlyList<(string ClientId, Uri Uri)>?)backChannel ?? []);
    }

    /// <summary>
    /// Sends a signed <c>logout_token</c> to every back-channel target in parallel, bounded by
    /// <see cref="BackChannelTimeout"/>. A slow or unreachable relying party never fails or blocks the
    /// user's own sign-out — failures are logged and otherwise swallowed.
    /// </summary>
    public async Task NotifyBackChannelAsync(string sessionId,
        IReadOnlyList<(string ClientId, Uri Uri)> targets, CancellationToken cancellationToken)
    {
        if (targets.Count == 0)
        {
            return;
        }

        using var timeout = new CancellationTokenSource(BackChannelTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        await Task.WhenAll(targets.Select(target => SendBackChannelLogoutAsync(sessionId, target, linked.Token)))
            .ConfigureAwait(false);
    }

    private async Task SendBackChannelLogoutAsync(string sessionId, (string ClientId, Uri Uri) target,
        CancellationToken cancellationToken)
    {
        try
        {
            var logoutToken = CreateLogoutToken(sessionId, target.ClientId);
            var client = httpClientFactory.CreateClient(BackChannelHttpClientName);
            using var content =
                new FormUrlEncodedContent([new KeyValuePair<string, string>("logout_token", logoutToken)]);
            using var response = await client.PostAsync(target.Uri, content, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Back-channel logout to client {ClientId} at {Uri} returned {StatusCode}.",
                    target.ClientId, target.Uri, (int)response.StatusCode);
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Back-channel logout to client {ClientId} at {Uri} failed.",
                target.ClientId, target.Uri);
        }
    }

    private string CreateLogoutToken(string sessionId, string audience)
    {
        var credentials = serverOptions.CurrentValue.SigningCredentials[0];
        var now = DateTimeOffset.UtcNow;

        // Normalized the same way OpenIddict's own SetIssuer(new Uri(...)) does (which, for a bare authority
        // with no path, adds a trailing slash) — this must match byte-for-byte what relying on parties already
        // know as this OP's issuer from discovery/their ID tokens' iss claim.
        var issuer = new Uri(huiaOptions.Issuer, UriKind.Absolute).AbsoluteUri;

        var payload = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            [OpenIddictConstants.Claims.Issuer] = issuer,
            // sub is deliberately omitted — per the Back-Channel Logout 1.0 spec a logout_token only needs
            // sub and/or sid, and sid alone is what keeps this session-scoped (see this class's own doc
            // comment): a relying party that keyed its own revocable session storage by sub instead would
            // revoke every device at once instead of just the one that signed out.
            [SessionClaimTypes.Sid] = sessionId,
            [OpenIddictConstants.Claims.Audience] = audience,
            [OpenIddictConstants.Claims.IssuedAt] = now.ToUnixTimeSeconds(),
            // Not required by the Back-Channel Logout 1.0 spec (a logout_token's freshness is really carried
            // by iat/jti), but a short expiry is harmless and some relying-party libraries validate it anyway.
            [OpenIddictConstants.Claims.ExpiresAt] = now.AddMinutes(2).ToUnixTimeSeconds(),
            [OpenIddictConstants.Claims.JwtId] = Guid.NewGuid().ToString("N"),
            // Per the OpenID Connect Back-Channel Logout 1.0 spec, this exact key with an empty object value
            // is what marks the token as a logout_token rather than an ordinary ID token.
            ["events"] = new Dictionary<string, object>
            {
                ["http://schemas.openid.net/event/backchannel-logout"] = new Dictionary<string, object>(),
            },
        });

        return new JsonWebTokenHandler().CreateToken(payload, credentials);
    }

    private static bool TryGetUri(IReadOnlyDictionary<string, JsonElement> properties, string propertyName,
        out Uri uri)
    {
        uri = null!;
        return properties.TryGetValue(propertyName, out var element)
               && element.ValueKind == JsonValueKind.String
               && Uri.TryCreate(element.GetString(), UriKind.Absolute, out uri!);
    }
}

/// <summary>
/// Other relying parties to notify of a logout, split by notification channel.
/// </summary>
public sealed record LogoutTargets(
    IReadOnlyList<Uri> FrontChannelLogoutUris,
    IReadOnlyList<(string ClientId, Uri Uri)> BackChannelLogoutTargets);