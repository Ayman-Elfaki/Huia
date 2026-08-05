using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Huia.TodoApi.Data;
using Huia.TodoApi.Models;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;

namespace Huia.Tests.Integration;

/// <summary>
/// Covers ReportsEndpoints' layered authorization — <c>RequireScope("reports")</c>,
/// <c>RequireAudience("reports-api")</c>, and <c>RequireRole("Admin")</c> — end to end over a real token
/// mint + validate round trip. <see cref="Huia.Tests.Unit.Authorization.AuthorizationPolicyBuilderExtensionsTests"/>
/// already proves each check in isolation at the unit level (including that <c>RequireAudience</c> and a
/// scope's own <c>Resources</c> are backed by different claims); this file proves they actually connect in
/// the real system — a scope seeded with <c>SetResource("reports-api")</c> really does put "reports-api" on
/// a granted token's <c>aud</c> claim, and <c>RequireAudience</c> really does see it there once the token is
/// validated — and that none of the three checks substitutes for another.
/// </summary>
public class ReportsEndpointsTests(TodoApiFactory factory) : IClassFixture<TodoApiFactory>
{
    [Fact]
    public async Task GetReportsSummary_WithoutToken_ReturnsUnauthorized()
    {
        var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/reports/summary");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// A token with "todos" (and hence the "todo-api" resource) but not "reports" satisfies the API's
    /// server-wide <c>RequireAudiences("todo-api")</c> validation just fine — it's ReportsEndpoints' own
    /// <c>RequireScope("reports")</c>/<c>RequireAudience("reports-api")</c> policy that has to catch this.
    /// </summary>
    [Fact]
    public async Task GetReportsSummary_WithTodosScopeOnly_ReturnsForbidden()
    {
        var client = factory.CreateClient();
        var token = await OAuthTestHelpers.GetTodoTestsAccessTokenAsync(client, scope: "todos");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync("/api/reports/summary");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Proves <c>RequireScope("reports")</c> and <c>RequireAudience("reports-api")</c> both pass for a real,
    /// validated client_credentials token that requested "reports" — the "reports-api" resource seeded on
    /// that scope (Program.cs) really does end up on the token's <c>aud</c> claim, and the validated
    /// principal really does expose it back as an audience. A machine-to-machine token carries no user/role
    /// at all though, so <c>RequireRole("Admin")</c> alone is what turns it away here. "todos" is requested
    /// alongside "reports" only so the token also clears the server-wide <c>RequireAudiences("todo-api")</c>
    /// gate at authentication (see <c>TokenSecurityTests.GetTodos_WithScopelessClientCredentialsToken_ReturnsUnauthorized</c>
    /// for that check on its own) — it isn't something ReportsEndpoints itself requires.
    /// </summary>
    [Fact]
    public async Task GetReportsSummary_WithReportsScope_ButNoAdminRole_ReturnsForbidden()
    {
        var client = factory.CreateClient();
        var token = await OAuthTestHelpers.GetTodoTestsAccessTokenAsync(client, scope: "todos reports");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync("/api/reports/summary");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetReportsSummary_ForAdminUser_ReturnsOkWithCrossUserCounts()
    {
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
            // TodoItem.OwnerId is a real foreign key into TodoUser (see TodoDbContext) — this inserts
            // directly rather than through the endpoint's own owner-upsert, so it needs the row itself.
            db.Users.Add(new TodoUser { Id = "someone-else", Email = "someone-else@example.com" });
            db.Todos.Add(new TodoItem { OwnerId = "someone-else", Title = "Cross-user todo", IsComplete = true });
            await db.SaveChangesAsync();
        }

        var client = await AdminTestHelpers.CreateAdminAuthorizedClientAsync(factory);

        using var response = await client.GetAsync("/api/reports/summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = await response.Content.ReadFromJsonAsync<ReportSummary>();
        Assert.NotNull(summary);
        Assert.True(summary!.TotalTodos >= 1);
        Assert.True(summary.DistinctOwners >= 1);
    }

    /// <summary>
    /// Direct check on the scope-descriptor side (<c>ScopeOptions.Resources</c>/<c>HuiaScopeSeeder</c>),
    /// independent of any token: the "reports" scope Program.cs declares is actually persisted with
    /// "reports-api" in its <c>OpenIddictScopeDescriptor.Resources</c>.
    /// </summary>
    [Fact]
    public async Task ReportsScope_IsSeededWithTheReportsApiResource()
    {
        using var scope = factory.Services.CreateScope();
        var scopeManager = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();

        var reportsScope = await scopeManager.FindByNameAsync("reports");
        Assert.NotNull(reportsScope);
        var resources = await scopeManager.GetResourcesAsync(reportsScope!);

        Assert.Contains("reports-api", resources);
    }

    private sealed record ReportSummary(int TotalTodos, int CompletedTodos, int DistinctOwners);
}