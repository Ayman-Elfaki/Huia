using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Huia.Events;
using Huia.IntegrationTests.Infrastructure;
using Shouldly;
using Microsoft.IdentityModel.JsonWebTokens;
using Xunit;

namespace Huia.IntegrationTests;

/// <summary>Claims assignment to users through <c>/master/admin/*</c>.</summary>
public sealed class AdminClaimsTests : IAsyncLifetime
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
    public async Task A_claim_can_be_added_to_a_user_listed_and_removed()
    {
        using var client = await AdminApiAsync();
        var userId = await _host.SeedUserAsync("acme", "claimsuser@acme.test", "Password1!");

        // Add a single claim
        var addRes = await client.PostAsJsonAsync($"/master/admin/users/{userId}/claims", new
        {
            type = "department",
            value = "engineering"
        });
        addRes.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await _host.Events.WaitForCountAsync<UserUpdatedEvent>(1, e => e.UserId == userId && e.TenantId == "acme");

        // List claims
        var claims = await client.GetFromJsonAsync<ClaimDto[]>($"/master/admin/users/{userId}/claims");
        claims.ShouldNotBeNull();
        claims.ShouldContain(c => c.Type == "department" && c.Value == "engineering");

        // Re-adding the exact same claim is idempotent
        var reAddRes = await client.PostAsJsonAsync($"/master/admin/users/{userId}/claims", new
        {
            type = "department",
            value = "engineering"
        });
        reAddRes.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Remove the claim by type and value
        var removeRes = await client.DeleteAsync($"/master/admin/users/{userId}/claims/department?value=engineering");
        removeRes.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await _host.Events.WaitForCountAsync<UserUpdatedEvent>(2, e => e.UserId == userId && e.TenantId == "acme");

        // Idempotent remove
        var reRemoveRes = await client.DeleteAsync($"/master/admin/users/{userId}/claims/department?value=engineering");
        reRemoveRes.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var claimsAfterRemove = await client.GetFromJsonAsync<ClaimDto[]>($"/master/admin/users/{userId}/claims");
        claimsAfterRemove.ShouldNotBeNull();
        claimsAfterRemove.ShouldNotContain(c => c.Type == "department");
    }

    [Fact]
    public async Task Adding_multiple_claims_at_once()
    {
        using var client = await AdminApiAsync();
        var userId = await _host.SeedUserAsync("acme", "batchclaims@acme.test", "Password1!");

        var addBatchRes = await client.PostAsJsonAsync($"/master/admin/users/{userId}/claims", new
        {
            claims = new[]
            {
                new { type = "tier", value = "gold" },
                new { type = "region", value = "eu-west" }
            }
        });
        addBatchRes.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await _host.Events.WaitForCountAsync<UserUpdatedEvent>(1, e => e.UserId == userId && e.TenantId == "acme");

        var claims = await client.GetFromJsonAsync<ClaimDto[]>($"/master/admin/users/{userId}/claims");
        claims.ShouldNotBeNull();
        claims.ShouldContain(c => c.Type == "tier" && c.Value == "gold");
        claims.ShouldContain(c => c.Type == "region" && c.Value == "eu-west");

        // Remove all claims of a type
        var removeTierRes = await client.DeleteAsync($"/master/admin/users/{userId}/claims/tier");
        removeTierRes.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var claimsAfter = await client.GetFromJsonAsync<ClaimDto[]>($"/master/admin/users/{userId}/claims");
        claimsAfter.ShouldNotBeNull();
        claimsAfter.ShouldNotContain(c => c.Type == "tier");
        claimsAfter.ShouldContain(c => c.Type == "region" && c.Value == "eu-west");

        // Remove by query endpoint
        var removeRegionRes = await client.DeleteAsync($"/master/admin/users/{userId}/claims?type=region&value=eu-west");
        removeRegionRes.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var claimsFinal = await client.GetFromJsonAsync<ClaimDto[]>($"/master/admin/users/{userId}/claims");
        claimsFinal.ShouldNotBeNull();
        claimsFinal.ShouldBeEmpty();
    }

    [Fact]
    public async Task Adding_claim_validates_type()
    {
        using var client = await AdminApiAsync();
        var userId = await _host.SeedUserAsync("acme", "invalidclaim@acme.test", "Password1!");

        var emptyRes = await client.PostAsJsonAsync($"/master/admin/users/{userId}/claims", new
        {
            type = "",
            value = "test"
        });
        emptyRes.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var noClaimsRes = await client.PostAsJsonAsync($"/master/admin/users/{userId}/claims", new { });
        noClaimsRes.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var removeMissingQueryRes = await client.DeleteAsync($"/master/admin/users/{userId}/claims");
        removeMissingQueryRes.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Claims_on_unknown_user_returns_404()
    {
        using var client = await AdminApiAsync();

        var getRes = await client.GetAsync("/master/admin/users/nonexistent/claims");
        getRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var postRes = await client.PostAsJsonAsync("/master/admin/users/nonexistent/claims", new { type = "t", value = "v" });
        postRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var deleteRes = await client.DeleteAsync("/master/admin/users/nonexistent/claims/t");
        deleteRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_non_administrator_cannot_touch_claims()
    {
        var flow = new AuthCodeFlow(_host, "acme", "acme-spa", RedirectUri);
        await _host.SeedInteractiveClientAsync("acme", "acme-spa", RedirectUri);
        var peonId = await _host.SeedUserAsync("acme", "peon2@acme.test", "Password1!", emailConfirmed: true);
        using var tokens = await flow.SignInAsync("peon2@acme.test", "Password1!", "openid profile email roles");
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens.RootElement.GetProperty("access_token").GetString());

        var response = await client.PostAsJsonAsync($"/acme/admin/users/{peonId}/claims", new { type = "sneaky", value = "val" });
        response.StatusCode.ShouldBeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Stored_claims_are_copied_into_identity_and_emitted_in_tokens_and_userinfo()
    {
        using var adminClient = await AdminApiAsync();
        await _host.SeedInteractiveClientAsync("acme", "claims-app", RedirectUri);
        var userId = await _host.SeedUserAsync("acme", "claimstester@acme.test", "Password1!", emailConfirmed: true);

        // Add stored claims to user
        var addRes = await adminClient.PostAsJsonAsync($"/master/admin/users/{userId}/claims", new
        {
            claims = new[]
            {
                new { type = "department", value = "engineering" },
                new { type = "tier", value = "gold" }
            }
        });
        addRes.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Sign in via authorization code flow
        var flow = new AuthCodeFlow(_host, "acme", "claims-app", RedirectUri);
        using var tokens = await flow.SignInAsync("claimstester@acme.test", "Password1!", "openid profile email offline_access");

        var accessToken = tokens.RootElement.GetProperty("access_token").GetString()!;
        var idToken = tokens.RootElement.GetProperty("id_token").GetString()!;
        var refreshToken = tokens.RootElement.GetProperty("refresh_token").GetString()!;

        // Verify claims in access token
        var accessJwt = new JsonWebToken(accessToken);
        accessJwt.GetClaim("department").Value.ShouldBe("engineering");
        accessJwt.GetClaim("tier").Value.ShouldBe("gold");

        // Verify claims in id token
        var idJwt = new JsonWebToken(idToken);
        idJwt.GetClaim("department").Value.ShouldBe("engineering");
        idJwt.GetClaim("tier").Value.ShouldBe("gold");

        // Verify claims in UserInfo endpoint
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var userInfo = await client.GetFromJsonAsync<JsonElement>("/acme/connect/userinfo");
        userInfo.GetProperty("department").GetString().ShouldBe("engineering");
        userInfo.GetProperty("tier").GetString().ShouldBe("gold");

        // Verify claims survive refresh token exchange
        using var refreshed = await flow.RefreshAsync(refreshToken);
        var refreshedAccess = new JsonWebToken(refreshed.RootElement.GetProperty("access_token").GetString()!);
        refreshedAccess.GetClaim("department").Value.ShouldBe("engineering");
        refreshedAccess.GetClaim("tier").Value.ShouldBe("gold");
    }

    private sealed record ClaimDto(string Type, string Value);
}
