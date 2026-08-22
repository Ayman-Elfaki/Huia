using Huia.Common;
using Huia.Pagination;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using OpenIddict.Abstractions;

namespace Huia.Endpoints.Admin;

/// <summary>
/// JSON CRUD endpoints over OpenIddict scopes, backed by <see cref="IOpenIddictScopeManager"/> (store-agnostic
/// — works against whatever <c>IHuiaStore</c>/EF Core backend is configured). 
/// registers — editing them here has the same effect on <c>aud</c> claims for tokens granted the scope.
/// </summary>
internal static class ScopesEndpoints
{
    public static IEndpointRouteBuilder MapScopesEndpoints(this IEndpointRouteBuilder api)
    {
        var scopes = api.MapGroup("/scopes");

        scopes.MapGet("", ListAsync);
        scopes.MapGet("/{id}", GetAsync);
        scopes.MapPost("", CreateAsync);
        scopes.MapPut("/{id}", UpdateAsync);
        scopes.MapDelete("/{id}", DeleteAsync);

        return api;
    }

    private static async Task<IResult> ListAsync(string? cursor, int? pageSize, IOpenIddictScopeManager manager,
        [FromServices] IAdminEfCorePaginator? paginator, CancellationToken cancellationToken)
    {
        var size = pageSize is null or <= 0 or > 100 ? 25 : pageSize.Value;

        if (paginator is not null)
        {
            return await paginator.ListScopesAsync(size, cancellationToken).ConfigureAwait(false);
        }

        var offset = OffsetCursor.Decode(cursor);

        // Fetches one extra row to know whether a next page exists, rather than inferring it from TotalCount.
        var scopes = new List<object>();
        await foreach (var scope in manager.ListAsync(size + 1, offset, cancellationToken).ConfigureAwait(false))
        {
            scopes.Add(scope);
        }

        var nextCursor = scopes.Count > size ? OffsetCursor.Encode(offset + size) : null;
        var items = new List<ScopeResponse>(size);
        foreach (var scope in scopes.Take(size))
        {
            items.Add(await ToResponseAsync(scope, manager, cancellationToken).ConfigureAwait(false));
        }

        var totalCount = (int)await manager.CountAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(new PagedResult<ScopeResponse>(items, totalCount, size, offset > 0, nextCursor is not null,
            nextCursor));
    }

    private static async Task<IResult> GetAsync(string id, IOpenIddictScopeManager manager,
        CancellationToken cancellationToken)
    {
        var scope = await manager.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        return scope is null
            ? Results.NotFound()
            : Results.Ok(await ToResponseAsync(scope, manager, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<IResult> CreateAsync(ScopeRequest request, IOpenIddictScopeManager manager,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.Problem("A scope name is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (await manager.FindByNameAsync(request.Name, cancellationToken).ConfigureAwait(false) is not null)
        {
            return Results.Problem($"A scope named '{request.Name}' already exists.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var descriptor = BuildDescriptor(request);
        var scope = await manager.CreateAsync(descriptor, cancellationToken).ConfigureAwait(false);
        return Results.Created(
            $"/api/scopes/{await manager.GetIdAsync(scope, cancellationToken).ConfigureAwait(false)}",
            await ToResponseAsync(scope, manager, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<IResult> UpdateAsync(string id, ScopeRequest request, IOpenIddictScopeManager manager,
        CancellationToken cancellationToken)
    {
        var scope = await manager.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (scope is null)
        {
            return Results.NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.Problem("A scope name is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var existing = await manager.FindByNameAsync(request.Name, cancellationToken).ConfigureAwait(false);
        if (existing is not null &&
            !string.Equals(await manager.GetIdAsync(existing, cancellationToken).ConfigureAwait(false), id,
                StringComparison.Ordinal))
        {
            return Results.Problem($"A scope named '{request.Name}' already exists.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var descriptor = BuildDescriptor(request);
        await manager.UpdateAsync(scope, descriptor, cancellationToken).ConfigureAwait(false);
        return Results.Ok(await ToResponseAsync(scope, manager, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<IResult> DeleteAsync(string id, IOpenIddictScopeManager manager,
        CancellationToken cancellationToken)
    {
        var scope = await manager.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (scope is null)
        {
            return Results.NotFound();
        }

        await manager.DeleteAsync(scope, cancellationToken).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static OpenIddictScopeDescriptor BuildDescriptor(ScopeRequest request)
    {
        var descriptor = new OpenIddictScopeDescriptor
        {
            Name = request.Name,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description,
        };

        foreach (var resource in request.Resources ?? [])
        {
            descriptor.Resources.Add(resource);
        }

        return descriptor;
    }

    internal static async Task<ScopeResponse> ToResponseAsync(object scope, IOpenIddictScopeManager manager,
        CancellationToken cancellationToken)
        => new(
            (await manager.GetIdAsync(scope, cancellationToken).ConfigureAwait(false))!,
            (await manager.GetNameAsync(scope, cancellationToken).ConfigureAwait(false))!,
            await manager.GetDisplayNameAsync(scope, cancellationToken).ConfigureAwait(false),
            await manager.GetDescriptionAsync(scope, cancellationToken).ConfigureAwait(false),
            [.. await manager.GetResourcesAsync(scope, cancellationToken).ConfigureAwait(false)]);

    internal sealed record ScopeResponse(
        string Id,
        string Name,
        string? DisplayName,
        string? Description,
        string[] Resources);

    private sealed record ScopeRequest(string Name, string? DisplayName, string? Description, string[]? Resources);
}