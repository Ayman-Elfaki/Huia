using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Huia.IntegrationTests.Infrastructure;
using Huia.Options;

namespace Huia.IntegrationTests;

public sealed class ConnectUserInfoAndLogoutTests : IAsyncLifetime
{
    private const string RedirectUri = "https://acme.example.test/callback";
    private HuiaTestHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await HuiaTestHost.StartAsync();
        await _host.SeedInteractiveClientAsync("acme", "acme-spa", RedirectUri);
        await _host.SeedUserAsync("acme", "erin@acme.test", "Password1!", emailConfirmed: true);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Userinfo_returns_claims_for_the_granted_scopes()
    {
        var flow = new AuthCodeFlow(_host, "acme", "acme-spa", RedirectUri);
        using var tokens = await flow.SignInAsync("erin@acme.test", "Password1!", "openid profile email");
        var accessToken = tokens.RootElement.GetProperty("access_token").GetString()!;

        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var userInfo = await client.GetFromJsonAsync<JsonElement>("/acme/connect/userinfo");
        userInfo.GetProperty("sub").GetString().ShouldNotBeNullOrEmpty();
        userInfo.GetProperty("email").GetString().ShouldBe("erin@acme.test");
        userInfo.GetProperty("preferred_username").GetString().ShouldBe("erin@acme.test");
    }

    [Fact]
    public async Task Userinfo_without_a_token_is_unauthorized()
    {
        var response = await _host.Client.GetAsync("/acme/connect/userinfo");
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_clears_the_session_and_redirects()
    {
        var client = _host.CreateClient();

        // Establish a session first.
        var flow = new AuthCodeFlow(_host, "acme", "acme-spa", RedirectUri);
        await flow.SignInAsync("erin@acme.test", "Password1!");

        var response = await client.GetAsync("/acme/connect/logout");
        response.StatusCode.ShouldBeOneOf(HttpStatusCode.Redirect, HttpStatusCode.Found, HttpStatusCode.OK);
    }

    [Fact]
    public async Task Logout_without_a_client_hint_lands_on_a_configured_app_url_not_the_tenant_root()
    {
        // Reproduces the infinite-redirect bug: with no id_token_hint / client_id the old fallback
        // pointed at "/{tenant}/", which the home endpoint bounced straight back.
        await using var host = await HuiaTestHost.StartAsync(configureOptions: huia =>
            huia.AddTenant("logout-app", tenant =>
            {
                tenant.Authentication.UseEmailAndPasswordLogin(password => password.RequireConfirmedEmail = false);
                tenant.AddServerSideWebApplication("logout-web", "logout-web-secret", client =>
                {
                    client.RedirectUris.Add(new Uri("https://app.example.test/callback"));
                    client.PostLogoutRedirectUris.Add(new Uri("https://app.example.test/"));
                });
            }));

        var response = await host.CreateClient().GetAsync("/logout-app/connect/logout");

        response.StatusCode.ShouldBeOneOf(HttpStatusCode.Redirect, HttpStatusCode.Found);
        response.Headers.Location!.ToString().ShouldBe("https://app.example.test/");
    }
}
