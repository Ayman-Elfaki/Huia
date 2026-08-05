using Huia.Common;
using Microsoft.AspNetCore.Http;
using OpenIddict.Abstractions;

namespace Huia.Sessions;

/// <summary>
/// Orchestrates session lifecycle on top of <see cref="IUserSessionManager"/>: created at interactive
/// sign-in (see <c>Huia.Identity.HuiaSignInManager</c>), checked/touched at token issuance (the connect
/// endpoints), and revoked at sign-out (<c>LogoutModel</c>, <c>EndSessionEndpoints</c>). Public because it's
/// injected into those (public) sign-in Razor Pages — not itself a customization point the way
/// <see cref="IUserSessionManager"/> is; register a different <see cref="IUserSessionManager"/> to
/// change storage, not this.
/// </summary>
public sealed class UserSessionService(
    IUserSessionManager manager,
    UserSessionsOptions options,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Creates a new session for <paramref name="userId"/>, best-effort recording the request's IP/user agent.
    /// </summary>
    public async Task<string> CreateAsync(string userId, HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = httpContext.Request.Headers.TryGetValue("User-Agent", out var values)
            ? values.ToString()
            : null;

        var descriptor = await manager.CreateAsync(userId, now, now + options.AbsoluteLifetime, ipAddress, userAgent,
                cancellationToken)
            .ConfigureAwait(false);

        return descriptor.Id;
    }

    /// <summary>
    /// Whether <paramref name="sessionId"/> still refers to a non-revoked, unexpired session.
    /// </summary>
    public async Task<bool> IsLiveAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var session = await manager.FindByIdAsync(sessionId, cancellationToken).ConfigureAwait(false);
        return session is not null && session.IsLive(timeProvider.GetUtcNow());
    }

    /// <summary>
    /// Bumps the session's last-activity timestamp — called whenever it's used to mint a token
    /// (authorization code, refresh token, or device code redemption).
    /// </summary>
    public Task TouchAsync(string sessionId, CancellationToken cancellationToken = default) =>
        manager.TouchAsync(sessionId, timeProvider.GetUtcNow(), cancellationToken);

    /// <summary>
    /// Ends a session immediately rather than waiting out its <see cref="UserSessionsOptions.AbsoluteLifetime"/>.
    /// </summary>
    public Task RevokeAsync(string sessionId, CancellationToken cancellationToken = default) =>
        manager.RevokeAsync(sessionId, timeProvider.GetUtcNow(), cancellationToken);

    /// <summary>
    /// An explicit revoke of someone else's live session — an admin acting on a user, or a user acting on
    /// one of their own other sessions — which is a strictly stronger action than that session's own
    /// end-user "Sign out": it additionally notifies every relying party tied to the session over the
    /// back channel (see <see cref="LogoutNotifier"/>) and revokes every OpenIddict authorization tied to
    /// it, rather than leaving them technically valid until this session's own liveness check
    /// (<see cref="IsLiveAsync"/>/the token endpoint) catches it. There's no front-channel notification here
    /// (unlike <c>EndSessionEndpoints</c>) — that needs a live browser to load an iframe into, which an
    /// out-of-band API call triggering this has no way to provide.
    /// </summary>
    public async Task RevokeWithNotificationAsync(UserSessionDescriptor session, LogoutNotifier logoutNotifier,
        IOpenIddictAuthorizationManager authorizationManager, CancellationToken cancellationToken = default)
    {
        var targets = await logoutNotifier
            .CollectAsync(session.UserId, session.Id, excludeClientId: null, cancellationToken)
            .ConfigureAwait(false);
        await logoutNotifier.NotifyBackChannelAsync(session.Id, targets.BackChannelLogoutTargets, cancellationToken)
            .ConfigureAwait(false);

        await foreach (var authorization in SessionAuthorizationLookup.FindBySessionAsync(
                           authorizationManager, session.UserId, session.Id, cancellationToken).ConfigureAwait(false))
        {
            await authorizationManager.TryRevokeAsync(authorization, cancellationToken).ConfigureAwait(false);
        }

        await RevokeAsync(session.Id, cancellationToken).ConfigureAwait(false);
    }
}