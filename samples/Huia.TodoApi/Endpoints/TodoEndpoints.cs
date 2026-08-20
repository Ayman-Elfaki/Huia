using System.Security.Claims;
using Huia.Identity;
using Huia.TodoApi.Data;
using Huia.TodoApi.Models;
using Microsoft.EntityFrameworkCore;
using MR.AspNetCore.Pagination;
using OpenIddict.Abstractions;

namespace Huia.TodoApi.Endpoints;


/// <summary>
/// Minimal API CRUD endpoints for todos, scoped to the caller's access token.
/// </summary>
public static class TodoEndpoints
{
    public sealed record TodoResponse(Guid Id, string Title, bool IsComplete, DateTime CreatedAt)
    {
        public static TodoResponse FromEntity(TodoItem item) => new(item.Id, item.Title, item.IsComplete, item.CreatedAt);
    }

    public sealed record CreateTodoRequest(string Title);

    public sealed record UpdateTodoRequest(string Title, bool IsComplete);

    
    // Long because it's a flat list of five independent MapGet/Post/Put/Delete route registrations -
    // splitting it would just relocate, not reduce, that sequence.
#pragma warning disable MA0051
    public static RouteGroupBuilder MapTodoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/todos")
            .RequireAuthorization(policy => policy.RequireScope("todos"))
            .WithTags("Todos");

        group.MapGet("/", async (ClaimsPrincipal user, TodoDbContext db, IPaginationService pagination,
            CancellationToken ct) =>
        {
            var ownerId = GetOwnerId(user);
            var todos = db.Todos.Where(t => t.OwnerId == ownerId);

            // CreatedAt alone can collide within the same tick - Id breaks ties so the keyset ordering stays
            // fully deterministic, which keyset pagination requires.
            var page = await pagination.KeysetPaginateAsync(
                todos,
                b => b.Ascending(t => t.CreatedAt).Ascending(t => t.Id),
                id => Guid.TryParse(id, out var todoId)
                    ? db.Todos.FirstOrDefaultAsync(t => t.Id == todoId, ct)
                    : Task.FromResult<TodoItem?>(null),
                q => q,
                pageSize: null).ConfigureAwait(false);

            return Results.Ok(new KeysetPaginationResult<TodoResponse>(
                [.. page.Data.Select(TodoResponse.FromEntity)],
                page.TotalCount, page.PageSize, page.HasPrevious, page.HasNext));
        })
        .WithSummary("List the caller's todos");

        group.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, TodoDbContext db, CancellationToken ct) =>
        {
            var ownerId = GetOwnerId(user);
            var todo = await db.Todos.FirstOrDefaultAsync(t => t.Id == id && t.OwnerId == ownerId, ct);

            return todo is null ? Results.NotFound() : Results.Ok(TodoResponse.FromEntity(todo));
        })
        .WithSummary("Get a single todo by id");

        group.MapPost("/", async (CreateTodoRequest request, ClaimsPrincipal user, TodoDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Title))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    [nameof(request.Title)] = ["Title is required."]
                });
            }

            var ownerId = GetOwnerId(user);
            await EnsureOwnerExistsAsync(ownerId, user, db, ct);

            var todo = new TodoItem { OwnerId = ownerId, Title = request.Title };
            db.Todos.Add(todo);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/todos/{todo.Id}", TodoResponse.FromEntity(todo));
        })
        .WithSummary("Create a todo");

        group.MapPut("/{id:guid}", async (Guid id, UpdateTodoRequest request, ClaimsPrincipal user, TodoDbContext db, CancellationToken ct) =>
        {
            var ownerId = GetOwnerId(user);
            var todo = await db.Todos.FirstOrDefaultAsync(t => t.Id == id && t.OwnerId == ownerId, ct);
            if (todo is null)
            {
                return Results.NotFound();
            }

            if (string.IsNullOrWhiteSpace(request.Title))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    [nameof(request.Title)] = ["Title is required."]
                });
            }

            todo.Title = request.Title;
            todo.IsComplete = request.IsComplete;
            await db.SaveChangesAsync(ct);

            return Results.Ok(TodoResponse.FromEntity(todo));
        })
        .WithSummary("Replace a todo's title/completed state");

        group.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, TodoDbContext db, CancellationToken ct) =>
        {
            var ownerId = GetOwnerId(user);
            // No need to materialize the row first: the response doesn't use it, just whether one matched.
            var deleted = await db.Todos.Where(t => t.Id == id && t.OwnerId == ownerId).ExecuteDeleteAsync(ct);

            return deleted == 0 ? Results.NotFound() : Results.NoContent();
        })
        .WithSummary("Delete a todo");

        return group;
    }
#pragma warning restore MA0051

    private static string GetOwnerId(ClaimsPrincipal user) =>
        user.GetClaim(OpenIddictConstants.Claims.Subject)
        ?? throw new InvalidOperationException("The access token is missing a 'sub' claim.");

    // TodoItem.OwnerId is a real foreign key into TodoUser (see TodoDbContext), normally populated ahead of
    // time by TodoUserRegisteredHandler reacting to UserRegisteredEvent. Machine-to-machine callers
    // (client_credentials grants) never register and so never raise that event — their "sub" is just the
    // client id — so this upserts a row on first write instead of letting the FK constraint fail.
    private static async Task EnsureOwnerExistsAsync(string ownerId, ClaimsPrincipal user, TodoDbContext db,
        CancellationToken ct)
    {
        var exists = await db.Users.AnyAsync(u => u.Id == ownerId, ct);
        if (exists)
        {
            return;
        }

        var email = user.GetClaim(OpenIddictConstants.Claims.Email) ?? $"{ownerId}@service.local";
        db.Users.Add(new TodoUser { Id = ownerId, Email = email });
    }
}
