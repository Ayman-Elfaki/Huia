using System.Net;
using System.Text.RegularExpressions;
using Huia.IntegrationTests.Infrastructure;
using Huia.Options;

namespace Huia.IntegrationTests;

/// <summary>
/// Covers the "you're signed in" landing page: it resumes a pending <c>/connect/authorize</c> when one
/// is carried in on <c>ReturnUrl</c>, and otherwise offers a link back to the tenant's client
/// application. The passkey management page carries the same "back to the app" link.
/// </summary>
public sealed partial class SignedInPageTests : IAsyncLifetime
{
    private const string Home = "https://landing.example.test/";

    private HuiaTestHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await HuiaTestHost.StartAsync(configureOptions: huia =>
            huia.AddTenant("landing", tenant =>
            {
                tenant.Authentication.UseEmailAndPasswordLogin(password => password.RequireConfirmedEmail = false);
                tenant.Authentication.UsePasskeyLogin();
                tenant.AddSinglePageApplication("landing-spa", client =>
                {
                    client.RedirectUris.Add(new Uri(Home + "callback"));
                    client.HomeUris.Add(new Uri(Home));
                });
            }));

        await _host.SeedUserAsync("landing", "sam@landing.test", "Password1!", emailConfirmed: true);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task It_resumes_a_pending_authorize_request()
    {
        var browser = await SignedInBrowserAsync();
        var pending = "/landing/connect/authorize?response_type=code&client_id=landing-spa&scope=openid";

        var response = await browser.GetAsync($"/landing/identity/account/signedin?ReturnUrl={Uri.EscapeDataString(pending)}");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().ShouldBe(pending);
    }

    [Fact]
    public async Task It_offers_a_continue_link_to_the_client_when_no_flow_is_pending()
    {
        var browser = await SignedInBrowserAsync();

        var response = await browser.GetAsync("/landing/identity/account/signedin");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.ShouldContain("signed-in-continue");
        html.ShouldContain(Home);
    }

    [Fact]
    public async Task It_ignores_a_non_local_return_url_and_renders_the_page()
    {
        var browser = await SignedInBrowserAsync();

        var response = await browser.GetAsync("/landing/identity/account/signedin?ReturnUrl=https%3A%2F%2Fevil.example%2F");

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_passkey_page_links_back_to_the_app()
    {
        var browser = await SignedInBrowserAsync();

        var response = await browser.GetAsync("/landing/identity/account/passkeys");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.ShouldContain("passkeys-home");
        html.ShouldContain(Home);
    }

    private async Task<HttpClient> SignedInBrowserAsync()
    {
        var browser = _host.CreateClient();
        const string loginUrl = "/landing/identity/account/login";

        var login = await browser.PostAsync(loginUrl, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = "sam@landing.test",
            ["Input.Password"] = "Password1!",
            ["__RequestVerificationToken"] = ExtractToken(await browser.GetStringAsync(loginUrl)),
        }));

        // The sign-in cookie is issued before the post-sign-in redirect is chosen (which, on this first
        // sign-in with passkeys on, points at the enrollment prompt) — the session is live either way.
        login.StatusCode.ShouldBe(HttpStatusCode.Redirect, await login.Content.ReadAsStringAsync());
        return browser;
    }

    private static string ExtractToken(string html) =>
        TokenRegex().Match(html) is { Success: true } m ? m.Groups["v"].Value : throw new InvalidOperationException("no antiforgery token");

    [GeneratedRegex("""name="__RequestVerificationToken"[^>]*value="(?<v>[^"]+)""")]
    private static partial Regex TokenRegex();
}
