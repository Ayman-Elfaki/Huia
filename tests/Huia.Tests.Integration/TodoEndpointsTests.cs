using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Huia.TodoApi.Data;
using Huia.TodoApi.Endpoints;
using Huia.TodoApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.Tests.Integration;


public class TodoEndpointsTests(TodoApiFactory factory) : IClassFixture<TodoApiFactory>
{
    [Fact]
    public async Task GetTodos_WithoutToken_ReturnsUnauthorized()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/todos");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Regression coverage for a bug the IDOR tests below caught: <c>UseHuia()</c> used to re-execute
    /// every empty-bodied error response through the branded (GET-only) error page regardless of the
    /// original request's HTTP method, which turned a POST/PUT/DELETE's correct 401/404 into an opaque,
    /// empty 400 — see <c>ApplicationBuilderExtensions.UseHuia</c>. Fixed by scoping that re-execution to
    /// GET requests only.
    /// </summary>
    [Fact]
    public async Task PostTodos_WithoutToken_ReturnsUnauthorized()
    {
        var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/todos", new TodoEndpoints.CreateTodoRequest("x"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PutTodos_WithoutToken_ReturnsUnauthorized()
    {
        var client = factory.CreateClient();

        using var response = await client.PutAsJsonAsync($"/api/todos/{Guid.NewGuid()}", new TodoEndpoints.UpdateTodoRequest("x", false));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteTodos_WithoutToken_ReturnsUnauthorized()
    {
        var client = factory.CreateClient();

        using var response = await client.DeleteAsync($"/api/todos/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Crud_FullLifecycle_Succeeds()
    {
        var client = await CreateAuthorizedClientAsync();

        var created = await client.PostAsJsonAsync("/api/todos", new TodoEndpoints.CreateTodoRequest("Buy milk"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var todo = await created.Content.ReadFromJsonAsync<TodoEndpoints.TodoResponse>();
        Assert.NotNull(todo);
        Assert.Equal("Buy milk", todo!.Title);
        Assert.False(todo.IsComplete);

        var fetched = await client.GetFromJsonAsync<TodoEndpoints.TodoResponse>($"/api/todos/{todo.Id}");
        Assert.Equal(todo.Id, fetched!.Id);

        var updated = await client.PutAsJsonAsync($"/api/todos/{todo.Id}", new TodoEndpoints.UpdateTodoRequest("Buy oat milk", true));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var updatedTodo = await updated.Content.ReadFromJsonAsync<TodoEndpoints.TodoResponse>();
        Assert.Equal("Buy oat milk", updatedTodo!.Title);
        Assert.True(updatedTodo.IsComplete);

        var deleted = await client.DeleteAsync($"/api/todos/{todo.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var afterDelete = await client.GetAsync($"/api/todos/{todo.Id}");
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
    }

    [Fact]
    public async Task CreateTodo_WithBlankTitle_ReturnsValidationProblem()
    {
        var client = await CreateAuthorizedClientAsync();

        var response = await client.PostAsJsonAsync("/api/todos", new TodoEndpoints.CreateTodoRequest("   "));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetTodos_DoesNotReturnAnotherOwners_Todo()
    {
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
            await EnsureTodoUserAsync(db, "someone-else");
            db.Todos.Add(new TodoItem { OwnerId = "someone-else", Title = "Not yours" });
            await db.SaveChangesAsync();
        }

        var client = await CreateAuthorizedClientAsync();

        var todos = await client.GetFromJsonAsync<List<TodoEndpoints.TodoResponse>>("/api/todos");

        Assert.DoesNotContain(todos!, t => t.Title == "Not yours");
    }

    /// <summary>
    /// Broken Object Level Authorization (OWASP API1:2023): <see cref="GetTodos_DoesNotReturnAnotherOwners_Todo"/>
    /// already covers the list endpoint; these prove the by-id read/write/delete endpoints — each a
    /// separate authorization check in <c>TodoEndpoints.cs</c> — can't be used to read, modify, or delete
    /// another owner's todo by simply guessing/enumerating its id.
    /// </summary>
    [Fact]
    public async Task GetTodoById_ForAnotherOwnersTodo_ReturnsNotFound()
    {
        var otherTodoId = await SeedAnotherOwnersTodoAsync();
        var client = await CreateAuthorizedClientAsync();

        var response = await client.GetAsync($"/api/todos/{otherTodoId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateTodo_ForAnotherOwnersTodo_ReturnsNotFoundAndLeavesItUnchanged()
    {
        var otherTodoId = await SeedAnotherOwnersTodoAsync();
        var client = await CreateAuthorizedClientAsync();

        var response = await client.PutAsJsonAsync($"/api/todos/{otherTodoId}", new TodoEndpoints.UpdateTodoRequest("Hijacked", true));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertTodoUnchangedAsync(otherTodoId);
    }

    [Fact]
    public async Task DeleteTodo_ForAnotherOwnersTodo_ReturnsNotFoundAndLeavesItInPlace()
    {
        var otherTodoId = await SeedAnotherOwnersTodoAsync();
        var client = await CreateAuthorizedClientAsync();

        var response = await client.DeleteAsync($"/api/todos/{otherTodoId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertTodoUnchangedAsync(otherTodoId);
    }

    private async Task<Guid> SeedAnotherOwnersTodoAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
        await EnsureTodoUserAsync(db, "someone-else");
        var todo = new TodoItem { OwnerId = "someone-else", Title = "Not yours" };
        db.Todos.Add(todo);
        await db.SaveChangesAsync();
        return todo.Id;
    }

    // TodoItem.OwnerId is a real foreign key into TodoUser (see TodoDbContext) — these tests insert todos
    // directly rather than through the endpoint's own owner-upsert (EnsureOwnerExistsAsync in
    // TodoEndpoints.cs), so they need to satisfy that constraint themselves.
    private static async Task EnsureTodoUserAsync(TodoDbContext db, string ownerId)
    {
        if (!await db.Users.AnyAsync(u => u.Id == ownerId))
        {
            db.Users.Add(new TodoUser { Id = ownerId, Email = $"{ownerId}@example.com" });
            await db.SaveChangesAsync();
        }
    }

    private async Task AssertTodoUnchangedAsync(Guid todoId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
        var todo = await db.Todos.FindAsync(todoId);
        Assert.NotNull(todo);
        Assert.Equal("Not yours", todo!.Title);
        Assert.False(todo.IsComplete);
    }

    private async Task<HttpClient> CreateAuthorizedClientAsync()
    {
        var client = factory.CreateClient();
        var token = await OAuthTestHelpers.GetTodoTestsAccessTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
