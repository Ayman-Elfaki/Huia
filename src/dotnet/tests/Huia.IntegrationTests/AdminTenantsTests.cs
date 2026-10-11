using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Huia.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.IntegrationTests;

/// <summary>Tenant create / read / update / delete through <c>/master/admin/tenants</c>.</summary>
public sealed class AdminTenantsTests : IAsyncLifetime
{
    private const string RedirectUri = "https://admin.example.test/callback";
    private HuiaTestHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await HuiaTestHost.StartAsync(configureBuilder: builder => builder.AddPersistedTenants());
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

    private static object Document(string displayName = "Initech", string? workerSecret = "worker-secret-value") => new
    {
        displayName,
        emailPassword = new { minimumLength = 10 },
        roles = new[] { "support" },
        applications = new[]
        {
            new
            {
                kind = "m2m",
                clientId = "initech-worker",
                clientSecret = workerSecret,
            },
        },
    };

    [Fact]
    public async Task A_tenant_can_be_created_read_updated_and_deleted_and_is_usable_at_once()
    {
        using var client = await AdminApiAsync();

        var create = await client.PostAsJsonAsync("/master/admin/tenants", new { tenantId = "initech", document = Document() });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);

        // Usable immediately: it appears in the list as dynamic and issues a token for its seeded client.
        var list = await client.GetFromJsonAsync<JsonElement>("/master/admin/tenants");
        var listed = list.EnumerateArray().Single(t => t.GetProperty("tenantId").GetString() == "initech");
        listed.GetProperty("origin").GetString().ShouldBe("dynamic");
        listed.GetProperty("clientCount").GetInt32().ShouldBe(1);

        var token = await _host.Client.PostAsync("/initech/connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = "initech-worker",
                ["client_secret"] = "worker-secret-value",
            }));
        token.StatusCode.ShouldBe(HttpStatusCode.OK, await token.Content.ReadAsStringAsync());

        // Read: the document comes back with the secret redacted.
        var read = await client.GetFromJsonAsync<JsonElement>("/master/admin/tenants/initech");
        read.GetProperty("origin").GetString().ShouldBe("dynamic");
        read.GetProperty("version").GetInt64().ShouldBe(1);
        var worker = read.GetProperty("document").GetProperty("applications")[0];
        worker.GetProperty("clientId").GetString().ShouldBe("initech-worker");
        worker.TryGetProperty("clientSecret", out var secret).ShouldBeTrue();
        secret.ValueKind.ShouldBe(JsonValueKind.Null);

        // Update without re-sending the secret: the stored one is kept.
        var update = await client.PutAsJsonAsync("/master/admin/tenants/initech",
            new { document = Document("Initech Two", workerSecret: null) });
        update.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var reread = await client.GetFromJsonAsync<JsonElement>("/master/admin/tenants/initech");
        reread.GetProperty("document").GetProperty("displayName").GetString().ShouldBe("Initech Two");
        reread.GetProperty("version").GetInt64().ShouldBe(2);

        var stillWorks = await _host.Client.PostAsync("/initech/connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = "initech-worker",
                ["client_secret"] = "worker-secret-value",
            }));
        stillWorks.StatusCode.ShouldBe(HttpStatusCode.OK);

        var delete = await client.DeleteAsync("/master/admin/tenants/initech");
        delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetAsync("/master/admin/tenants/initech")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _host.Client.GetAsync("/initech/.well-known/openid-configuration")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_code_defined_tenant_is_readable_but_not_modifiable()
    {
        using var client = await AdminApiAsync();

        var read = await client.GetFromJsonAsync<JsonElement>("/master/admin/tenants/master");
        read.GetProperty("origin").GetString().ShouldBe("static");

        (await client.PutAsJsonAsync("/master/admin/tenants/master", new { document = Document() })).StatusCode
            .ShouldBe(HttpStatusCode.Conflict);
        (await client.DeleteAsync("/master/admin/tenants/master")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Creating_a_tenant_validates_the_id_and_the_document()
    {
        using var client = await AdminApiAsync();

        (await client.PostAsJsonAsync("/master/admin/tenants", new { tenantId = "Bad Id", document = Document() }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync("/master/admin/tenants", new { tenantId = "connect", document = Document() }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync("/master/admin/tenants", new { tenantId = "master", document = Document() }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // A confidential client without a secret does not validate.
        var noSecret = await client.PostAsJsonAsync("/master/admin/tenants",
            new { tenantId = "nosecret", document = Document(workerSecret: null) });
        noSecret.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
