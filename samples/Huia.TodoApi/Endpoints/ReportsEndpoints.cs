using Huia.Identity;
using Huia.TodoApi.Data;
using Microsoft.EntityFrameworkCore;

namespace Huia.TodoApi.Endpoints;

/// <summary>
/// A cross-user reporting endpoint, gated far more tightly than <c>TodoEndpoints</c>: those endpoints only
/// require the "todos" scope and filter every query down to the caller's own <c>sub</c>, so any signed-in
/// user can reach them. This one requires all three of a distinct scope ("reports"), a distinct audience
/// ("reports-api" — see its registration in Program.cs), and the "Admin" role, to illustrate how the three
/// line up: scope says what the client (the token) is allowed to ask for, audience says which resource the
/// token was actually minted for, and role says who the signed-in user is. All three are checked
/// independently — none of them substitutes for another.
/// </summary>
public static class ReportsEndpoints
{
    public static RouteGroupBuilder MapReportsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/reports")
            .RequireAuthorization(policy => policy
                .RequireScope("reports")
                .RequireAudience("reports-api")
                .RequireRole("Admin"))
            .WithTags("Reports");

        group.MapGet("/summary", async (TodoDbContext db, CancellationToken ct) =>
            {
                var totalTodos = await db.Todos.CountAsync(ct);
                var completedTodos = await db.Todos.CountAsync(t => t.IsComplete, ct);
                var distinctOwners = await db.Todos.Select(t => t.OwnerId).Distinct().CountAsync(ct);

                return Results.Ok(new ReportSummaryResponse(totalTodos, completedTodos, distinctOwners));
            })
            .WithSummary("Aggregate todo counts across every user — admin-only");

        return group;
    }
}

public sealed record ReportSummaryResponse(int TotalTodos, int CompletedTodos, int DistinctOwners);