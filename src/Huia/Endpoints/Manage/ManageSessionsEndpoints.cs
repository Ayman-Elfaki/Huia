using System.Security.Claims;
using Huia.Applications;
using Huia.Common;
using Huia.Identity;
using Huia.Sessions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using OpenIddict.Abstractions;

namespace Huia.Endpoints.Manage;

/// <summary>
/// JSON endpoints for a signed-in user to see and revoke their <em>own</em> sessions — the self-service
/// counterpart to <c>Endpoints.Admin.SessionsEndpoints</c>, which acts on any user's sessions but requires
/// an admin caller. Lets a client build a "Security" or "Devices" settings page: every other session (a
/// different browser/device that's still signed in via SSO) can be revoked individually, or all at once via
/// <c>/revoke-others</c> ("sign out of all other devices") without touching the one making the request.
/// </summary>
internal static class ManageSessionsEndpoints
{
    public static RouteGroupBuilder MapManageSessionsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/sessions");

        group.MapGet("", ListAsync);
        group.MapPost("/{id}/revoke", RevokeAsync);
        group.MapPost("/revoke-others", RevokeOthersAsync);

        return group;
    }

    // A self-service listing has no paging UI to drive — a user isn't going to have hundreds of live
    // sessions — so this simply caps how many come back rather than exposing page/pageSize like the admin
    // endpoint does.
    private const int MaxSessions = 100;

    private static async Task<IResult> ListAsync(ClaimsPrincipal principal, UserManager<HuiaUser> userManager,
        IUserSessionManager manager, IOpenIddictAuthorizationManager authorizationManager,
        IOpenIddictApplicationManager applicationManager, CancellationToken cancellationToken)
    {
        var user = await userManager.GetSignedInUserAsync(principal).ConfigureAwait(false);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var userId = await userManager.GetUserIdAsync(user).ConfigureAwait(false);
        var currentSessionId = principal.FindFirstValue(SessionClaimTypes.Sid);

        var items = await manager.ListAsync(MaxSessions, 0, userId, cancellationToken).ConfigureAwait(false);

        var clientIdCache = new Dictionary<string, string?>(StringComparer.Ordinal);
        var responses = new List<SessionResponse>(items.Count);
        foreach (var session in items)
        {
            responses.Add(await ToResponseAsync(session, currentSessionId, authorizationManager, applicationManager,
                clientIdCache, cancellationToken).ConfigureAwait(false));
        }

        return Results.Ok(responses);
    }

    private static async Task<IResult> RevokeAsync(string id, ClaimsPrincipal principal,
        UserManager<HuiaUser> userManager, IUserSessionManager manager, UserSessionService sessions,
        LogoutNotifier logoutNotifier, IOpenIddictAuthorizationManager authorizationManager,
        CancellationToken cancellationToken)
    {
        var user = await userManager.GetSignedInUserAsync(principal).ConfigureAwait(false);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var userId = await userManager.GetUserIdAsync(user).ConfigureAwait(false);
        var session = await manager.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);

        // Someone else's session id gets exactly the same response as one that doesn't exist — same
        // "don't confirm existence" reasoning as, say, a password-reset request for an unregistered email.
        if (session is null || session.UserId != userId)
        {
            return Results.NotFound();
        }

        await sessions.RevokeWithNotificationAsync(session, logoutNotifier, authorizationManager, cancellationToken)
            .ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> RevokeOthersAsync(ClaimsPrincipal principal, UserManager<HuiaUser> userManager,
        IUserSessionManager manager, UserSessionService sessions, LogoutNotifier logoutNotifier,
        IOpenIddictAuthorizationManager authorizationManager, CancellationToken cancellationToken)
    {
        var user = await userManager.GetSignedInUserAsync(principal).ConfigureAwait(false);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var userId = await userManager.GetUserIdAsync(user).ConfigureAwait(false);
        var currentSessionId = principal.FindFirstValue(SessionClaimTypes.Sid);

        // No page cap here (unlike ListAsync above) — this needs to reach every live session for the user,
        // not just the first page of them. Mirrors Admin.SessionsEndpoints.RevokeAllAsync.
        const int batchSize = 100;
        var offset = 0;
        while (true)
        {
            var batch = await manager.ListAsync(batchSize, offset, userId, cancellationToken).ConfigureAwait(false);
            if (batch.Count == 0)
            {
                break;
            }

            foreach (var session in batch.Where(s => s.RevokedAt is null && s.Id != currentSessionId))
            {
                await sessions.RevokeWithNotificationAsync(session, logoutNotifier, authorizationManager,
                    cancellationToken).ConfigureAwait(false);
            }

            offset += batchSize;
        }

        return Results.NoContent();
    }

    /// <summary>
    /// A session isn't tied to a single application — SSO lets it back authorizations for several clients —
    /// so this surfaces every distinct application's <c>client_id</c> the session has authorized, rather than
    /// a single (and otherwise meaningless) internal application id. <see cref="SessionResponse.IsCurrent"/>
    /// is what lets a "Devices" UI mark ("This device") and disable revoking the session backing the request
    /// that's asking.
    /// </summary>
    private static async Task<SessionResponse> ToResponseAsync(UserSessionDescriptor session,
        string? currentSessionId, IOpenIddictAuthorizationManager authorizationManager,
        IOpenIddictApplicationManager applicationManager, Dictionary<string, string?> clientIdCache,
        CancellationToken cancellationToken)
    {
        var clientIds = new List<string>();
        await foreach (var authorization in SessionAuthorizationLookup
                           .FindBySessionAsync(authorizationManager, session.UserId, session.Id, cancellationToken)
                           .ConfigureAwait(false))
        {
            var applicationId = await authorizationManager.GetApplicationIdAsync(authorization, cancellationToken)
                .ConfigureAwait(false);
            var clientId = await ApplicationClientIdResolver
                .ResolveAsync(applicationId, applicationManager, clientIdCache, cancellationToken)
                .ConfigureAwait(false);
            if (clientId is not null && !clientIds.Contains(clientId, StringComparer.Ordinal))
            {
                clientIds.Add(clientId);
            }
        }

        return new SessionResponse(session.Id, session.CreatedAt, session.LastActivityAt, session.ExpiresAt,
            session.IpAddress, session.UserAgent, session.RevokedAt, session.Id == currentSessionId, [.. clientIds]);
    }

    private sealed record SessionResponse(
        string Id,
        DateTimeOffset CreatedAt,
        DateTimeOffset LastActivityAt,
        DateTimeOffset ExpiresAt,
        string? IpAddress,
        string? UserAgent,
        DateTimeOffset? RevokedAt,
        bool IsCurrent,
        string[] ApplicationClientIds);
}
