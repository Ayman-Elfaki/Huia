using Huia.IntegrationTests.Infrastructure;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Huia.IntegrationTests;

public sealed class AuthorizationCodeFlowTests : IAsyncLifetime
{
    private const string RedirectUri = "https://acme.example.test/callback";
    private HuiaTestHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await HuiaTestHost.StartAsync();
        await _host.SeedInteractiveClientAsync("acme", "acme-spa", RedirectUri);
        await _host.SeedUserAsync("acme", "alice@acme.test", "Password1!");
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task A_full_authorization_code_pkce_sign_in_yields_tenant_scoped_tokens()
    {
        var flow = new AuthCodeFlow(_host, "acme", "acme-spa", RedirectUri);

        using var tokens = await flow.SignInAsync("alice@acme.test", "Password1!");

        var accessToken = tokens.RootElement.GetProperty("access_token").GetString()!;
        var idToken = tokens.RootElement.GetProperty("id_token").GetString()!;

        var access = new JsonWebToken(accessToken);
        access.Issuer.ShouldEndWith("/acme");
        access.GetClaim("tenant").Value.ShouldBe("acme");

        var id = new JsonWebToken(idToken);
        id.GetClaim("email").Value.ShouldBe("alice@acme.test");

        tokens.RootElement.TryGetProperty("refresh_token", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Bad_credentials_re_render_the_login_page_with_an_error()
    {
        var flow = new AuthCodeFlow(_host, "acme", "acme-spa", RedirectUri);

        var html = await flow.AttemptFailedSignInAsync("alice@acme.test", "wrong-password");

        html.ShouldContain("not correct");
    }

    [Fact]
    public async Task The_refresh_token_can_be_exchanged_for_a_new_access_token()
    {
        var flow = new AuthCodeFlow(_host, "acme", "acme-spa", RedirectUri);
        using var tokens = await flow.SignInAsync("alice@acme.test", "Password1!");
        var refreshToken = tokens.RootElement.GetProperty("refresh_token").GetString()!;

        using var refreshed = await flow.RefreshAsync(refreshToken);

        refreshed.RootElement.GetProperty("access_token").GetString().ShouldNotBeNullOrEmpty();
    }
}
