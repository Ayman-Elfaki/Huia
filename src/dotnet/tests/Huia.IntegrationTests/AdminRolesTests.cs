using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Huia.IntegrationTests.Infrastructure;

namespace Huia.IntegrationTests;

/// <summary>Roles CRUD and user-role assignment through <c>/master/admin/*</c>.</summary>
public sealed class AdminRolesTests : IAsyncLifetime
{
    private const string RedirectUri = "https://admin.example.test/callback";
    private HuiaTestHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await HuiaTestHost.StartAsync();
        await _host.SeedInteractiveClientAsync("master", "master-spa", RedirectUri);
        var adminId = await _host.SeedUserAsync("master", "root@master.test", "Password1!", emailConfirmed: true);
        await _host.AssignRoleAsync("master", adminId, HuiaConstants.Roles.Administrator);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private async Task<HttpClient> AdminApiAsync()
    {
        var flow = new AuthCodeFlow(_host, "master", "master-spa", RedirectUri);
        using var tokens = await flow.SignInAsync("root@master.test", "Password1!", "openid profile email roles");
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens.RootElement.GetProperty("access_token").GetString());
        return client;
    }

    [Fact]
    public async Task A_role_can_be_created_listed_renamed_and_deleted()
    {
        using var client = await AdminApiAsync();

        var create = await client.PostAsJsonAsync("/master/admin/roles", new { tenant = "acme", name = "billing.viewer" });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        var listed = await client.GetFromJsonAsync<JsonElement>("/master/admin/roles?tenant=acme");
        listed.GetProperty("data").EnumerateArray()
            .Select(r => r.GetProperty("name").GetString()).ShouldContain("billing.viewer");

        var detail = await client.GetFromJsonAsync<JsonElement>($"/master/admin/roles/{id}");
        detail.GetProperty("memberCount").GetInt32().ShouldBe(0);

        var rename = await client.PutAsJsonAsync($"/master/admin/roles/{id}", new { name = "billing.reader" });
        rename.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var delete = await client.DeleteAsync($"/master/admin/roles/{id}");
        delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Duplicate_role_names_and_unknown_tenants_are_rejected()
    {
        using var client = await AdminApiAsync();

        var payload = new { tenant = "acme", name = "dup.role" };
        (await client.PostAsJsonAsync("/master/admin/roles", payload)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await client.PostAsJsonAsync("/master/admin/roles", payload)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        (await client.PostAsJsonAsync("/master/admin/roles", new { tenant = "nope", name = "x" }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_role_can_be_assigned_to_a_user_and_shows_up_on_the_user_record()
    {
        using var client = await AdminApiAsync();
        var userId = await _host.SeedUserAsync("acme", "member@acme.test", "Password1!");

        (await client.PostAsJsonAsync("/master/admin/roles", new { tenant = "acme", name = "reviewer" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var assign = await client.PostAsJsonAsync($"/master/admin/users/{userId}/roles", new { role = "reviewer" });
        assign.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var roles = await client.GetFromJsonAsync<string[]>($"/master/admin/users/{userId}/roles");
        roles.ShouldContain("reviewer");

        var user = await client.GetFromJsonAsync<JsonElement>($"/master/admin/users/{userId}");
        user.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ShouldContain("reviewer");

        var remove = await client.DeleteAsync($"/master/admin/users/{userId}/roles/reviewer");
        remove.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await client.GetFromJsonAsync<string[]>($"/master/admin/users/{userId}/roles")).ShouldNotContain("reviewer");
    }

    [Fact]
    public async Task Assigning_an_unknown_role_is_a_validation_problem()
    {
        using var client = await AdminApiAsync();
        var userId = await _host.SeedUserAsync("acme", "orphan@acme.test", "Password1!");

        var assign = await client.PostAsJsonAsync($"/master/admin/users/{userId}/roles", new { role = "ghost" });
        assign.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_role_with_members_cannot_be_deleted()
    {
        using var client = await AdminApiAsync();
        var userId = await _host.SeedUserAsync("acme", "holder@acme.test", "Password1!");

        var create = await client.PostAsJsonAsync("/master/admin/roles", new { tenant = "acme", name = "occupied" });
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        await client.PostAsJsonAsync($"/master/admin/users/{userId}/roles", new { role = "occupied" });

        var delete = await client.DeleteAsync($"/master/admin/roles/{id}");
        delete.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_non_administrator_cannot_touch_roles()
    {
        var flow = new AuthCodeFlow(_host, "acme", "acme-spa", RedirectUri);
        await _host.SeedInteractiveClientAsync("acme", "acme-spa", RedirectUri);
        await _host.SeedUserAsync("acme", "peon@acme.test", "Password1!", emailConfirmed: true);
        using var tokens = await flow.SignInAsync("peon@acme.test", "Password1!", "openid profile email roles");
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens.RootElement.GetProperty("access_token").GetString());

        var response = await client.PostAsJsonAsync("/acme/admin/roles", new { tenant = "acme", name = "sneaky" });
        response.StatusCode.ShouldBeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
    }
}
