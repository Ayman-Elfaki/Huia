using Huia.Common;
using Huia.Endpoints.Connect;
using Huia.Sessions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpenIddict.Abstractions;

namespace Huia.Endpoints.Admin;

/// <summary>
/// Read-only JSON endpoints over live sessions, plus revocation. Sessions are created by the sign-in flow
/// itself, not by an admin — there's no create endpoint here.
/// </summary>
internal static class SessionsEndpoints
{
    public static IEndpointRouteBuilder MapSessionsEndpoints(this IEndpointRouteBuilder api)
    {
        var sessions = api.MapGroup("/sessions");

        sessions.MapGet("", ListAsync);
        sessions.MapGet("/{id}", GetAsync);
        sessions.MapPost("/{id}/revoke", RevokeAsync);
        sessions.MapPost("/revoke-all", RevokeAllAsync);

        return api;
    }

    private static async Task<IResult> ListAsync(int? page, int? pageSize, string? subject,
        IUserSessionManager manager, CancellationToken cancellationToken)
    {
        var size = pageSize is null or <= 0 or > 100 ? 25 : pageSize.Value;
        var offset = ((page is null or <= 0 ? 1 : page.Value) - 1) * size;

        var items = await manager.ListAsync(size, offset, subject, cancellationToken).ConfigureAwait(false);
        var totalCount = await manager.CountAsync(subject, cancellationToken).ConfigureAwait(false);

        return Results.Ok(new PagedResult<SessionResponse>([.. items.Select(ToResponse)], totalCount));
    }

    private static async Task<IResult> GetAsync(string id, IUserSessionManager manager,
        CancellationToken cancellationToken)
    {
        var session = await manager.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        return session is null ? Results.NotFound() : Results.Ok(ToResponse(session));
    }

    private static async Task<IResult> RevokeAsync(string id, IUserSessionManager manager,
        UserSessionService sessions, LogoutNotifier logoutNotifier,
        IOpenIddictAuthorizationManager authorizationManager, CancellationToken cancellationToken)
    {
        var session = await manager.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return Results.NotFound();
        }

        await RevokeSessionAsync(session, sessions, logoutNotifier, authorizationManager, cancellationToken)
            .ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> RevokeAllAsync(RevokeAllRequest request, IUserSessionManager manager,
        UserSessionService sessions, LogoutNotifier logoutNotifier,
        IOpenIddictAuthorizationManager authorizationManager, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Subject))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Subject)] = ["A subject is required."],
            });
        }

        // No page cap here (unlike ListAsync) — an explicit "sign out everywhere" needs to reach every live
        // session for the user, not just the first page of them.
        const int batchSize = 100;
        var offset = 0;
        while (true)
        {
            var batch = await manager.ListAsync(batchSize, offset, request.Subject, cancellationToken)
                .ConfigureAwait(false);
            if (batch.Count == 0)
            {
                break;
            }

            foreach (var session in batch.Where(s => s.RevokedAt is null))
            {
                await RevokeSessionAsync(session, sessions, logoutNotifier, authorizationManager, cancellationToken)
                    .ConfigureAwait(false);
            }

            offset += batchSize;
        }

        return Results.NoContent();
    }

    /// <summary>
    /// Notifies the session's clients (same as a normal sign-out) and additionally revokes every OpenIddict
    /// authorization tied to it — an admin's explicit revoke is a strictly stronger action than an
    /// end-user's own "Sign out": that one only notifies (see <see cref="LogoutNotifier"/>'s own remarks),
    /// leaving the underlying authorization/refresh token technically valid at the OpenIddict layer (the
    /// session's own liveness check in <see cref="UserSessionService"/>/the token endpoint is what
    /// actually blocks its further use in that case).
    /// </summary>
    private static async Task RevokeSessionAsync(UserSessionDescriptor session, UserSessionService sessions,
        LogoutNotifier logoutNotifier, IOpenIddictAuthorizationManager manager,
        CancellationToken ct)
    {
        var targets = await logoutNotifier
            .CollectAsync(session.UserId, session.Id, excludeClientId: null, ct)
            .ConfigureAwait(false);
        await logoutNotifier.NotifyBackChannelAsync(session.Id, targets.BackChannelLogoutTargets, ct)
            .ConfigureAwait(false);

        await foreach (var authorization in SessionAuthorizationLookup.FindBySessionAsync(
                           manager, session.UserId, session.Id, ct).ConfigureAwait(false))
        {
            await manager.TryRevokeAsync(authorization, ct).ConfigureAwait(false);
        }

        await sessions.RevokeAsync(session.Id, ct).ConfigureAwait(false);
    }

    private static SessionResponse ToResponse(UserSessionDescriptor session) => new(
        session.Id, session.UserId, session.CreatedAt, session.LastActivityAt, session.ExpiresAt,
        session.IpAddress, session.UserAgent, session.RevokedAt);

    private sealed record SessionResponse(
        string Id,
        string UserId,
        DateTimeOffset CreatedAt,
        DateTimeOffset LastActivityAt,
        DateTimeOffset ExpiresAt,
        string? IpAddress,
        string? UserAgent,
        DateTimeOffset? RevokedAt);

    private sealed record RevokeAllRequest(string Subject);
}