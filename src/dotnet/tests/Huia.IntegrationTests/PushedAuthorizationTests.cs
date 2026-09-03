using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Huia.IntegrationTests.Infrastructure;
using Microsoft.IdentityModel.Tokens;

namespace Huia.IntegrationTests;

/// <summary>Pushed Authorization Requests (RFC 9126), configurable per application.</summary>
public sealed class PushedAuthorizationTests : IAsyncLifetime
{
    private const string RedirectUri = "https://par.example.test/callback";
    private HuiaTestHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await HuiaTestHost.StartAsync();
        await _host.SeedInteractiveClientAsync("acme", "par-optional", RedirectUri);
        await _host.SeedInteractiveClientAsync("acme", "par-required", RedirectUri, requirePushedAuthorization: true);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task The_discovery_document_advertises_the_par_endpoint()
    {
        var response = await _host.Client.GetAsync("/acme/.well-known/openid-configuration");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        doc.RootElement.GetProperty("pushed_authorization_request_endpoint").GetString()!
            .ShouldEndWith("/acme/connect/par");
    }

    [Fact]
    public async Task A_pushed_request_yields_a_request_uri_that_the_authorize_endpoint_accepts()
    {
        var verifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        var par = await _host.Client.PostAsync("/acme/connect/par", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = "par-optional",
            ["response_type"] = "code",
            ["redirect_uri"] = RedirectUri,
            ["scope"] = "openid profile",
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["state"] = "s",
        }));

        par.StatusCode.ShouldBe(HttpStatusCode.Created, await par.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await par.Content.ReadAsStringAsync());
        var requestUri = body.RootElement.GetProperty("request_uri").GetString()!;
        requestUri.ShouldStartWith("urn:ietf:params:oauth:request_uri:");
        body.RootElement.GetProperty("expires_in").GetInt32().ShouldBeGreaterThan(0);

        var authorize = await _host.Client.GetAsync(
            $"/acme/connect/authorize?client_id=par-optional&request_uri={Uri.EscapeDataString(requestUri)}");

        authorize.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        authorize.Headers.Location!.ToString().ShouldContain("/identity/account/login");
    }

    [Fact]
    public async Task A_par_required_client_is_rejected_at_the_authorize_endpoint_without_a_request_uri()
    {
        var authorize = await _host.Client.GetAsync(
            "/acme/connect/authorize?client_id=par-required&response_type=code" +
            $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}&scope=openid" +
            "&code_challenge=x&code_challenge_method=S256&state=s");

        var location = authorize.Headers.Location?.ToString() ?? string.Empty;
        var body = await authorize.Content.ReadAsStringAsync();

        location.ShouldNotContain("/identity/account/login");
        (location.Contains("error=", StringComparison.Ordinal) || body.Contains("invalid_request", StringComparison.Ordinal))
            .ShouldBeTrue($"expected a PAR-required error; status={authorize.StatusCode}, location={location}, body={body}");
    }
}
