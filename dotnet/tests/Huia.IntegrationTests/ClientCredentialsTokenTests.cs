using System.Net;
using System.Text.Json;
using Huia.IntegrationTests.Infrastructure;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Huia.IntegrationTests;

public sealed class ClientCredentialsTokenTests : IAsyncLifetime
{
    private HuiaTestHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await HuiaTestHost.StartAsync();
        await _host.SeedMachineClientAsync("acme", "acme-worker", "s3cret-value-1234");
        await _host.SeedMachineClientAsync("master", "master-worker", "s3cret-value-5678");
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task A_client_credentials_token_is_signed_with_the_tenant_key_and_carries_the_tenant_issuer()
    {
        var response = await RequestTokenAsync("acme", "acme-worker", "s3cret-value-1234");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var jwt = await ReadAccessTokenAsync(response);
        jwt.Issuer.ShouldEndWith("/acme");

        using var jwks = await GetJsonAsync("/acme/.well-known/jwks");
        var expectedKid = jwks.RootElement.GetProperty("keys")[0].GetProperty("kid").GetString();
        jwt.Kid.ShouldBe(expectedKid);
    }

    [Fact]
    public async Task An_invalid_client_secret_is_rejected_with_401()
    {
        var response = await RequestTokenAsync("acme", "acme-worker", "wrong-secret");
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_client_from_another_tenant_is_invisible()
    {
        var response = await RequestTokenAsync("master", "acme-worker", "s3cret-value-1234");
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Tokens_from_different_tenants_use_different_signing_keys()
    {
        var acme = await ReadAccessTokenAsync(await RequestTokenAsync("acme", "acme-worker", "s3cret-value-1234"));
        var master = await ReadAccessTokenAsync(await RequestTokenAsync("master", "master-worker", "s3cret-value-5678"));

        acme.Kid.ShouldNotBe(master.Kid);
        acme.Issuer.ShouldNotBe(master.Issuer);
    }

    private Task<HttpResponseMessage> RequestTokenAsync(string tenant, string clientId, string clientSecret) =>
        _host.Client.PostAsync($"/{tenant}/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
        }));

    private static async Task<JsonWebToken> ReadAccessTokenAsync(HttpResponseMessage response)
    {
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return new JsonWebToken(payload.RootElement.GetProperty("access_token").GetString());
    }

    private async Task<JsonDocument> GetJsonAsync(string path) =>
        JsonDocument.Parse(await (await _host.Client.GetAsync(path)).Content.ReadAsStringAsync());
}
