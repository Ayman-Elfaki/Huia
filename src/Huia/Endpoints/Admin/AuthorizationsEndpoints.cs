using Huia.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
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
        int? page, int? pageSize, string? subject, string? applicationId,
        IOpenIddictAuthorizationManager manager, CancellationToken cancellationToken)
    {
        var size = pageSize is null or <= 0 or > 100 ? 25 : pageSize.Value;
        var offset = ((page is null or <= 0 ? 1 : page.Value) - 1) * size;

        if (!string.IsNullOrWhiteSpace(subject) || !string.IsNullOrWhiteSpace(applicationId))
        {
            // The filtered lookups (FindBySubjectAsync/FindByApplicationIdAsync) don't support server-side
            // paging — fine for an admin-only surface where a given user/application has at most a handful
            // of live authorizations.
            var filtered = new List<object>();
            await foreach (var authorization in Find(manager, subject, applicationId, cancellationToken)
                               .ConfigureAwait(false))
            {
                filtered.Add(authorization);
            }

            var page1 = filtered.Skip(offset).Take(size).ToList();
            var responses = new List<AuthorizationResponse>(page1.Count);
            foreach (var authorization in page1)
            {
                responses.Add(await ToResponseAsync(authorization, manager, cancellationToken).ConfigureAwait(false));
            }

            return Results.Ok(new PagedResult<AuthorizationResponse>(responses, filtered.Count));
        }

        var items = new List<AuthorizationResponse>();
        await foreach (var authorization in manager.ListAsync(size, offset, cancellationToken).ConfigureAwait(false))
        {
            items.Add(await ToResponseAsync(authorization, manager, cancellationToken).ConfigureAwait(false));
        }

        var totalCount = await manager.CountAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(new PagedResult<AuthorizationResponse>(items, totalCount));
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

    private static async Task<AuthorizationResponse> ToResponseAsync(object authorization,
        IOpenIddictAuthorizationManager manager, CancellationToken cancellationToken)
        => new(
            (await manager.GetIdAsync(authorization, cancellationToken).ConfigureAwait(false))!,
            await manager.GetApplicationIdAsync(authorization, cancellationToken).ConfigureAwait(false),
            await manager.GetSubjectAsync(authorization, cancellationToken).ConfigureAwait(false),
            await manager.GetStatusAsync(authorization, cancellationToken).ConfigureAwait(false),
            await manager.GetTypeAsync(authorization, cancellationToken).ConfigureAwait(false),
            await manager.GetCreationDateAsync(authorization, cancellationToken).ConfigureAwait(false),
            [.. await manager.GetScopesAsync(authorization, cancellationToken).ConfigureAwait(false)]);

    private sealed record AuthorizationResponse(
        string Id,
        string? ApplicationId,
        string? Subject,
        string? Status,
        string? Type,
        DateTimeOffset? CreationDate,
        string[] Scopes);
}