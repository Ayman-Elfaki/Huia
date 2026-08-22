using Huia.Common;
using Huia.Pagination;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using OpenIddict.Abstractions;

namespace Huia.Endpoints.Admin;


/// <summary>
/// Read-only JSON endpoints over live OpenIddict authorizations, plus revocation, backed by
/// <see cref="IOpenIddictAuthorizationManager"/>. Authorizations are created by the OAuth flow itself, not
/// by an admin — there's no create endpoint here.
/// </summary>
internal static class AuthorizationsEndpoints
{
    public static IEndpointRouteBuilder MapAuthorizationsEndpoints(this IEndpointRouteBuilder api)
    {
        var authorizations = api.MapGroup("/authorizations");

        authorizations.MapGet("", ListAsync);
        authorizations.MapGet("/{id}", GetAsync);
        authorizations.MapPost("/{id}/revoke", RevokeAsync);

        return api;
    }

    private static async Task<IResult> ListAsync(
        string? cursor, int? pageSize, string? subject, string? applicationId,
        IOpenIddictAuthorizationManager manager, [FromServices] IAdminEfCorePaginator? paginator,
        CancellationToken cancellationToken)
    {
        var size = pageSize is null or <= 0 or > 100 ? 25 : pageSize.Value;

        if (paginator is not null)
        {
            return await paginator.ListAuthorizationsAsync(size, subject, applicationId, cancellationToken)
                .ConfigureAwait(false);
        }

        var offset = OffsetCursor.Decode(cursor);
        var isFiltered = !string.IsNullOrWhiteSpace(subject) || applicationId is not null;

        // Fetches one extra candidate (rather than inferring it from TotalCount) to know whether a next page
        // exists - see OffsetCursor's own doc comment for why this stays offset-based rather than a real
        // keyset query.
        int totalCount;
        List<object> candidates;
        if (isFiltered)
        {
            (candidates, totalCount) = await FindFilteredAsync(manager, subject, applicationId, offset, size,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            candidates = await ListUnfilteredAsync(manager, offset, size, cancellationToken).ConfigureAwait(false);
            totalCount = (int)await manager.CountAsync(cancellationToken).ConfigureAwait(false);
        }

        var nextCursor = candidates.Count > size ? OffsetCursor.Encode(offset + size) : null;
        var items = new List<AuthorizationResponse>(size);
        foreach (var authorization in candidates.Take(size))
        {
            items.Add(await ToResponseAsync(authorization, manager, cancellationToken).ConfigureAwait(false));
        }

        return Results.Ok(new PagedResult<AuthorizationResponse>(items, totalCount, size, offset > 0,
            nextCursor is not null, nextCursor));
    }

    /// <summary>
    /// The filtered lookups (FindBySubjectAsync/FindByApplicationIdAsync) don't support server-side paging —
    /// fine for an admin-only surface where a given user/application has at most a handful of live
    /// authorizations. Materializes every match to get a real <c>TotalCount</c> before slicing the requested
    /// page out of it.
    /// </summary>
    private static async Task<(List<object> Page, int TotalCount)> FindFilteredAsync(
        IOpenIddictAuthorizationManager manager, string? subject, string? applicationId, int offset, int size,
        CancellationToken cancellationToken)
    {
        var filtered = new List<object>();
        await foreach (var authorization in Find(manager, subject, applicationId, cancellationToken)
                           .ConfigureAwait(false))
        {
            filtered.Add(authorization);
        }

        return (filtered.Skip(offset).Take(size + 1).ToList(), filtered.Count);
    }

    private static async Task<List<object>> ListUnfilteredAsync(IOpenIddictAuthorizationManager manager, int offset,
        int size, CancellationToken cancellationToken)
    {
        var authorizations = new List<object>();
        await foreach (var authorization in manager.ListAsync(size + 1, offset, cancellationToken)
                           .ConfigureAwait(false))
        {
            authorizations.Add(authorization);
        }

        return authorizations;
    }

    private static IAsyncEnumerable<object> Find(IOpenIddictAuthorizationManager manager, string? subject,
        string? applicationId, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(subject))
        {
            return manager.FindBySubjectAsync(subject, cancellationToken);
        }

        return manager.FindByApplicationIdAsync(applicationId!, cancellationToken);
    }

    private static async Task<IResult> GetAsync(string id, IOpenIddictAuthorizationManager manager,
        CancellationToken cancellationToken)
    {
        var authorization = await manager.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        return authorization is null
            ? Results.NotFound()
            : Results.Ok(await ToResponseAsync(authorization, manager, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<IResult> RevokeAsync(string id, IOpenIddictAuthorizationManager manager,
        CancellationToken cancellationToken)
    {
        var authorization = await manager.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (authorization is null)
        {
            return Results.NotFound();
        }

        await manager.TryRevokeAsync(authorization, cancellationToken).ConfigureAwait(false);
        return Results.NoContent();
    }

    internal static async Task<AuthorizationResponse> ToResponseAsync(object authorization,
        IOpenIddictAuthorizationManager manager, CancellationToken cancellationToken)
        => new(
            (await manager.GetIdAsync(authorization, cancellationToken).ConfigureAwait(false))!,
            await manager.GetApplicationIdAsync(authorization, cancellationToken).ConfigureAwait(false),
            await manager.GetSubjectAsync(authorization, cancellationToken).ConfigureAwait(false),
            await manager.GetStatusAsync(authorization, cancellationToken).ConfigureAwait(false),
            await manager.GetTypeAsync(authorization, cancellationToken).ConfigureAwait(false),
            await manager.GetCreationDateAsync(authorization, cancellationToken).ConfigureAwait(false),
            [.. await manager.GetScopesAsync(authorization, cancellationToken).ConfigureAwait(false)]);

    /// <summary>Every field here is fetchable straight off <see cref="IOpenIddictAuthorizationManager"/> —
    /// <see cref="Subject"/> is the raw user id, not a resolved username (see <c>UsersEndpoints</c>), and
    /// <see cref="ApplicationId"/> is OpenIddict's own internal application id, not the application's public
    /// <c>client_id</c> (see <c>ApplicationsEndpoints</c>) — resolving either would need an extra manager
    /// beyond <see cref="IOpenIddictAuthorizationManager"/>.</summary>
    internal sealed record AuthorizationResponse(
        string Id,
        string? ApplicationId,
        string? Subject,
        string? Status,
        string? Type,
        DateTimeOffset? CreationDate,
        string[] Scopes);
}
