using System.Net;
using System.Text.Json;
using Huia.IntegrationTests.Infrastructure;

namespace Huia.IntegrationTests;

public sealed class OpenIddictDiscoveryTests : IAsyncLifetime
{
    private HuiaTestHost _host = null!;

    public async Task InitializeAsync() => _host = await HuiaTestHost.StartAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Theory]
    [InlineData("acme")]
    [InlineData("master")]
    public async Task The_discovery_document_is_served_per_tenant_with_a_tenant_scoped_issuer(string tenant)
    {
        using var document = await GetJsonAsync($"/{tenant}/.well-known/openid-configuration");
        var root = document.RootElement;

        root.GetProperty("issuer").GetString()!.ShouldEndWith($"/{tenant}");
        root.GetProperty("authorization_endpoint").GetString()!.ShouldContain($"/{tenant}/connect/authorize");
        root.GetProperty("token_endpoint").GetString()!.ShouldContain($"/{tenant}/connect/token");
        root.GetProperty("jwks_uri").GetString()!.ShouldContain($"/{tenant}/.well-known/jwks");
    }

    [Fact]
    public async Task Each_tenant_advertises_its_own_signing_key()
    {
        using var acme = await GetJsonAsync("/acme/.well-known/jwks");
        using var master = await GetJsonAsync("/master/.well-known/jwks");

        var acmeKid = acme.RootElement.GetProperty("keys")[0].GetProperty("kid").GetString();
        var masterKid = master.RootElement.GetProperty("keys")[0].GetProperty("kid").GetString();

        acmeKid.ShouldNotBeNullOrEmpty();
        masterKid.ShouldNotBeNullOrEmpty();
        acmeKid.ShouldNotBe(masterKid);

        acme.RootElement.GetProperty("keys")[0].GetProperty("kty").GetString().ShouldBe("RSA");
        acme.RootElement.GetProperty("keys")[0].GetProperty("use").GetString().ShouldBe("sig");
    }

    private async Task<JsonDocument> GetJsonAsync(string path)
    {
        var response = await _host.Client.GetAsync(path);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}
