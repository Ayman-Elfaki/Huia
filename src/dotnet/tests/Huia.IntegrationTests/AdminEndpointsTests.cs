using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Huia.IntegrationTests.Infrastructure;
using Huia.OpenId.Options;
using Huia.Options;

namespace Huia.IntegrationTests;

public sealed class AdminEndpointsTests : IAsyncLifetime
{
    private const string RedirectUri = "https://admin.example.test/callback";
    private HuiaTestHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await HuiaTestHost.StartAsync(configureOptions: huia =>
        {
            // A tenant carrying a declaratively-seeded ("static") scope, to prove the admin API
            // reports its origin and refuses to mutate it.
            huia.AddTenant("scoped-admin", tenant =>
            {
                tenant.Authentication.UseEmailAndPasswordLogin(password => password.RequireConfirmedEmail = false);
                tenant.AddHuiaOpenId(openId => openId.AddScope("reports:read", scope => scope.DisplayName = "Read reports"));
            });
        });
        await _host.SeedInteractiveClientAsync("master", "master-spa", RedirectUri);
        await _host.SeedInteractiveClientAsync("acme", "acme-spa", RedirectUri);

        var adminId = await _host.SeedUserAsync("master", "root@master.test", "Password1!", emailConfirmed: true);
        await _host.AssignRoleAsync("master", adminId, HuiaConstants.Roles.Administrator);
        await _host.SeedUserAsync("acme", "peon@acme.test", "Password1!", emailConfirmed: true);

        for (var i = 0; i < 7; i++)
        {
            await _host.SeedUserAsync("acme", $"user{i}@acme.test", "Password1!", emailConfirmed: true);
        }
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private async Task<HttpClient> AdminApiAsync()
    {
        var flow = new AuthCodeFlow(_host, "master", "master-spa", RedirectUri);
        using var tokens = await flow.SignInAsync("root@master.test", "Password1!", "openid profile email roles");
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.RootElement.GetProperty("access_token").GetString());
        return client;
    }

    [Fact]
    public async Task Anonymous_admin_calls_are_challenged()
    {
        var response = await _host.Client.GetAsync("/master/admin/users");
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_non_administrator_is_forbidden()
    {
        var flow = new AuthCodeFlow(_host, "acme", "acme-spa", RedirectUri);
        using var tokens = await flow.SignInAsync("peon@acme.test", "Password1!", "openid profile email roles");
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.RootElement.GetProperty("access_token").GetString());

        var response = await client.GetAsync("/acme/admin/users");
        response.StatusCode.ShouldBeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_tenant_list_is_returned()
    {
        using var client = await AdminApiAsync();
        var tenants = await client.GetFromJsonAsync<JsonElement>("/master/admin/tenants");

        tenants.EnumerateArray().Select(t => t.GetProperty("tenantId").GetString())
            .ShouldContain("acme");
    }

    [Fact]
    public async Task Users_are_returned_with_working_keyset_pagination()
    {
        using var client = await AdminApiAsync();

        var firstPage = await client.GetFromJsonAsync<JsonElement>("/master/admin/users?tenant=acme&size=3");
        firstPage.GetProperty("data").GetArrayLength().ShouldBe(3);
        firstPage.GetProperty("hasNext").GetBoolean().ShouldBeTrue();

        var lastId = firstPage.GetProperty("data")[2].GetProperty("id").GetString();
        var secondPage = await client.GetFromJsonAsync<JsonElement>($"/master/admin/users?tenant=acme&size=3&after={lastId}");

        secondPage.GetProperty("data")[0].GetProperty("id").GetString().ShouldNotBe(lastId);
    }

    [Fact]
    public async Task Keys_and_clients_lists_are_returned()
    {
        using var client = await AdminApiAsync();

        var keys = await client.GetFromJsonAsync<JsonElement>("/master/admin/keys");
        keys.GetProperty("data").GetArrayLength().ShouldBeGreaterThan(0);

        var clients = await client.GetFromJsonAsync<JsonElement>("/master/admin/clients");
        clients.GetProperty("data").GetArrayLength().ShouldBeGreaterThan(0);
        clients.GetProperty("data")[0].GetProperty("origin").GetString().ShouldBeOneOf("static", "dynamic");
    }

    [Fact]
    public async Task A_scope_can_be_created_listed_updated_and_deleted_for_a_tenant()
    {
        using var client = await AdminApiAsync();

        var create = await client.PostAsJsonAsync("/master/admin/scopes", new
        {
            tenant = "acme",
            name = "billing:read",
            displayName = "Read billing",
            resources = new[] { "billing-api" },
        });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);

        var listed = await client.GetFromJsonAsync<JsonElement>("/master/admin/scopes?tenant=acme");
        var created = listed.EnumerateArray().Single(s => s.GetProperty("name").GetString() == "billing:read");
        created.GetProperty("origin").GetString().ShouldBe("dynamic");

        var update = await client.PutAsJsonAsync("/master/admin/scopes/billing:read", new
        {
            tenant = "acme",
            displayName = "Read billing data",
        });
        update.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var delete = await client.DeleteAsync("/master/admin/scopes/billing:read?tenant=acme");
        delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var afterDelete = await client.GetFromJsonAsync<JsonElement>("/master/admin/scopes?tenant=acme");
        afterDelete.EnumerateArray().Select(s => s.GetProperty("name").GetString()).ShouldNotContain("billing:read");
    }

    [Fact]
    public async Task Code_defined_scopes_are_reported_as_static_and_cannot_be_modified()
    {
        using var client = await AdminApiAsync();

        var listed = await client.GetFromJsonAsync<JsonElement>("/master/admin/scopes?tenant=scoped-admin");
        var reports = listed.EnumerateArray().Single(s => s.GetProperty("name").GetString() == "reports:read");
        reports.GetProperty("origin").GetString().ShouldBe("static");

        var update = await client.PutAsJsonAsync("/master/admin/scopes/reports:read", new
        {
            tenant = "scoped-admin",
            displayName = "Tampered",
        });
        update.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var delete = await client.DeleteAsync("/master/admin/scopes/reports:read?tenant=scoped-admin");
        delete.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Creating_a_scope_for_an_unknown_tenant_is_a_validation_problem()
    {
        using var client = await AdminApiAsync();

        var response = await client.PostAsJsonAsync("/master/admin/scopes", new { tenant = "nope", name = "x" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Scope_writes_require_the_administrator_role()
    {
        var flow = new AuthCodeFlow(_host, "acme", "acme-spa", RedirectUri);
        using var tokens = await flow.SignInAsync("peon@acme.test", "Password1!", "openid profile email roles");
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.RootElement.GetProperty("access_token").GetString());

        var response = await client.PostAsJsonAsync("/acme/admin/scopes", new { tenant = "acme", name = "nope:read" });

        response.StatusCode.ShouldBeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
    }
}
