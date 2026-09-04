using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Huia.IntegrationTests.Infrastructure;

namespace Huia.IntegrationTests;

/// <summary>CRUD for users, clients and signing keys through <c>/master/admin/*</c>.</summary>
public sealed class AdminCrudTests : IAsyncLifetime
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

    // ------------------------------------------------------------------ users

    [Fact]
    public async Task A_password_user_can_be_created_read_updated_and_deleted()
    {
        using var client = await AdminApiAsync();

        var create = await client.PostAsJsonAsync("/master/admin/users", new
        {
            tenant = "acme",
            email = "newbie@acme.test",
            password = "Password1!",
            firstName = "New",
            lastName = "Bie",
            emailConfirmed = true,
            roles = new[] { "reporter" },
        });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        var read = await client.GetFromJsonAsync<JsonElement>($"/master/admin/users/{id}");
        read.GetProperty("email").GetString().ShouldBe("newbie@acme.test");
        read.GetProperty("emailConfirmed").GetBoolean().ShouldBeTrue();

        var update = await client.PutAsJsonAsync($"/master/admin/users/{id}", new { firstName = "Renamed", lockoutEnabled = true });
        update.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var delete = await client.DeleteAsync($"/master/admin/users/{id}");
        delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var afterDelete = await client.GetAsync($"/master/admin/users/{id}");
        afterDelete.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_phone_user_can_be_created()
    {
        using var client = await AdminApiAsync();

        var create = await client.PostAsJsonAsync("/master/admin/users", new
        {
            tenant = "phone",
            phoneNumber = "+15005550401",
            firstName = "Ph",
            lastName = "One",
        });

        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await create.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("userName").GetString().ShouldBe("+15005550401");
        body.GetProperty("phoneNumberConfirmed").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Creating_a_user_with_conflicting_contact_details_is_rejected()
    {
        using var client = await AdminApiAsync();

        var response = await client.PostAsJsonAsync("/master/admin/users", new
        {
            tenant = "acme",
            email = "mix@acme.test",
            password = "Password1!",
            phoneNumber = "+15005550402",
            firstName = "Mix",
            lastName = "Ed",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Creating_a_user_for_an_unknown_tenant_is_rejected()
    {
        using var client = await AdminApiAsync();

        var response = await client.PostAsJsonAsync("/master/admin/users", new
        {
            tenant = "nope",
            email = "x@nope.test",
            password = "Password1!",
            firstName = "X",
            lastName = "Y",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Creating_a_duplicate_user_is_a_conflict()
    {
        using var client = await AdminApiAsync();

        var payload = new { tenant = "acme", email = "dup@acme.test", password = "Password1!", firstName = "D", lastName = "U" };
        (await client.PostAsJsonAsync("/master/admin/users", payload)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await client.PostAsJsonAsync("/master/admin/users", payload)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_user_can_be_locked_and_unlocked()
    {
        using var client = await AdminApiAsync();

        var create = await client.PostAsJsonAsync("/master/admin/users", new
        {
            tenant = "acme",
            email = "lockme@acme.test",
            password = "Password1!",
            firstName = "Lock",
            lastName = "Me",
            emailConfirmed = true,
        });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        var lock_ = await client.PostAsync($"/master/admin/users/{id}/lock", null);
        lock_.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var afterLock = await client.GetFromJsonAsync<JsonElement>($"/master/admin/users/{id}");
        afterLock.GetProperty("lockoutEnabled").GetBoolean().ShouldBeTrue();
        afterLock.GetProperty("lockoutEnd").GetDateTimeOffset().ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddYears(1));

        var unlock = await client.PostAsync($"/master/admin/users/{id}/unlock", null);
        unlock.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var afterUnlock = await client.GetFromJsonAsync<JsonElement>($"/master/admin/users/{id}");
        afterUnlock.GetProperty("lockoutEnd").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task An_email_and_password_account_email_can_be_verified()
    {
        using var client = await AdminApiAsync();

        var create = await client.PostAsJsonAsync("/master/admin/users", new
        {
            tenant = "acme",
            email = "unverified@acme.test",
            password = "Password1!",
            firstName = "Un",
            lastName = "Verified",
            emailConfirmed = false,
        });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        var verify = await client.PostAsync($"/master/admin/users/{id}/verify-email", null);
        verify.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = await client.GetFromJsonAsync<JsonElement>($"/master/admin/users/{id}");
        after.GetProperty("emailConfirmed").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task A_phone_accounts_email_cannot_be_verified()
    {
        using var client = await AdminApiAsync();

        var create = await client.PostAsJsonAsync("/master/admin/users", new
        {
            tenant = "phone",
            phoneNumber = "+15005550403",
            firstName = "Ph",
            lastName = "One",
        });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        var verify = await client.PostAsync($"/master/admin/users/{id}/verify-email", null);
        verify.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ------------------------------------------------------------------ clients

    [Fact]
    public async Task A_client_can_be_created_read_updated_and_deleted()
    {
        using var client = await AdminApiAsync();

        var create = await client.PostAsJsonAsync("/master/admin/clients", new
        {
            tenant = "acme",
            clientId = "acme-dynamic",
            kind = "SinglePageApplication",
            redirectUris = new[] { "https://acme.example.test/cb" },
            scopes = new[] { "openid" },
        });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);

        var listed = await client.GetFromJsonAsync<JsonElement>("/master/admin/clients");
        var row = listed.GetProperty("data").EnumerateArray()
            .Single(c => c.GetProperty("clientId").GetString() == "acme-dynamic");
        row.GetProperty("origin").GetString().ShouldBe("dynamic");
        var id = row.GetProperty("id").GetString()!;

        var read = await client.GetFromJsonAsync<JsonElement>($"/master/admin/clients/{id}");
        read.GetProperty("redirectUris")[0].GetString().ShouldBe("https://acme.example.test/cb");

        var update = await client.PutAsJsonAsync($"/master/admin/clients/{id}", new
        {
            clientId = "acme-dynamic",
            displayName = "Acme Dynamic",
            kind = "SinglePageApplication",
            redirectUris = new[] { "https://acme.example.test/cb2" },
        });
        update.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var delete = await client.DeleteAsync($"/master/admin/clients/{id}");
        delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_code_seeded_client_cannot_be_modified()
    {
        using var client = await AdminApiAsync();

        var listed = await client.GetFromJsonAsync<JsonElement>("/master/admin/clients");
        var staticRow = listed.GetProperty("data").EnumerateArray()
            .First(c => c.GetProperty("origin").GetString() == "static");
        var id = staticRow.GetProperty("id").GetString()!;

        var update = await client.PutAsJsonAsync($"/master/admin/clients/{id}", new
        {
            clientId = staticRow.GetProperty("clientId").GetString(),
            kind = "SinglePageApplication",
            redirectUris = new[] { "https://x.example.test/cb" },
        });
        update.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var delete = await client.DeleteAsync($"/master/admin/clients/{id}");
        delete.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Creating_a_duplicate_client_is_a_conflict()
    {
        using var client = await AdminApiAsync();

        var payload = new
        {
            tenant = "acme",
            clientId = "acme-dup",
            kind = "SinglePageApplication",
            redirectUris = new[] { "https://acme.example.test/cb" },
        };
        (await client.PostAsJsonAsync("/master/admin/clients", payload)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await client.PostAsJsonAsync("/master/admin/clients", payload)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    // ------------------------------------------------------------------ keys

    [Fact]
    public async Task A_pending_key_can_be_created_and_read()
    {
        using var client = await AdminApiAsync();

        var create = await client.PostAsJsonAsync("/master/admin/keys", new { tenant = "acme" });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        var read = await client.GetFromJsonAsync<JsonElement>($"/master/admin/keys/{id}");
        read.GetProperty("status").GetString().ShouldBe("Pending");
    }

    [Fact]
    public async Task Activating_a_new_key_demotes_the_previous_active_key()
    {
        using var client = await AdminApiAsync();

        var create = await client.PostAsJsonAsync("/master/admin/keys", new { tenant = "acme", activate = true });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);

        var keys = await client.GetFromJsonAsync<JsonElement>("/master/admin/keys?tenant=acme");
        var statuses = keys.GetProperty("data").EnumerateArray()
            .Select(k => k.GetProperty("status").GetString()).ToList();

        statuses.Count(s => s == "Active").ShouldBe(1);
        statuses.ShouldContain("Rotated");
    }

    [Fact]
    public async Task Revoking_the_only_active_key_is_rejected()
    {
        using var client = await AdminApiAsync();

        var keys = await client.GetFromJsonAsync<JsonElement>("/master/admin/keys?tenant=acme");
        var activeId = keys.GetProperty("data").EnumerateArray()
            .Single(k => k.GetProperty("status").GetString() == "Active").GetProperty("id").GetString()!;

        var revoke = await client.PostAsync($"/master/admin/keys/{activeId}/revoke", content: null);
        revoke.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_key_can_be_revoked_once_a_replacement_exists_and_then_deleted()
    {
        using var client = await AdminApiAsync();

        var pending = await client.PostAsJsonAsync("/master/admin/keys", new { tenant = "acme" });
        var pendingId = (await pending.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        var deleteWhilePending = await client.DeleteAsync($"/master/admin/keys/{pendingId}");
        deleteWhilePending.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var revoke = await client.PostAsync($"/master/admin/keys/{pendingId}/revoke", content: null);
        revoke.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var delete = await client.DeleteAsync($"/master/admin/keys/{pendingId}");
        delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }
}
