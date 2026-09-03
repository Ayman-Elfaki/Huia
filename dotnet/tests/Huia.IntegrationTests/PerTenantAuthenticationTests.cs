using System.Net;
using System.Text.RegularExpressions;
using Huia.IntegrationTests.Infrastructure;

namespace Huia.IntegrationTests;

/// <summary>
/// Finbuckle per-tenant authentication: the interactive session cookie is named per tenant and a
/// session created under one tenant is not honoured under another (the ticket carries its origin
/// tenant and <c>OnValidatePrincipal</c> rejects a mismatch).
/// </summary>
public sealed partial class PerTenantAuthenticationTests : IAsyncLifetime
{
    private const string AcmeRedirect = "https://acme.example.test/callback";
    private const string ConsumerRedirect = "https://consumer.example.test/callback";

    private HuiaTestHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await HuiaTestHost.StartAsync();
        await _host.SeedInteractiveClientAsync("acme", "acme-spa", AcmeRedirect);
        await _host.SeedInteractiveClientAsync("consumer", "consumer-spa", ConsumerRedirect);
        await _host.SeedUserAsync("acme", "alice@acme.test", "Password1!");
        await _host.SeedUserAsync("consumer", "bob@consumer.test", "Password1!");
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task The_interactive_session_cookie_is_named_per_tenant()
    {
        var client = _host.CreateClient();

        var setCookie = await SignInAsync(client, "acme", "acme-spa", AcmeRedirect, "alice@acme.test", "Password1!");

        setCookie.ShouldContain("huia.auth.acme=");
        setCookie.ShouldNotContain("huia.auth=");
    }

    [Fact]
    public async Task A_session_from_one_tenant_does_not_authenticate_under_another()
    {
        var client = _host.CreateClient();
        await SignInAsync(client, "acme", "acme-spa", AcmeRedirect, "alice@acme.test", "Password1!");

        // Same browser, straight to consumer's authorize endpoint: it must bounce to the consumer
        // login page rather than issue a code off the acme session.
        var response = await client.GetAsync(AuthorizeUrl("consumer", "consumer-spa", ConsumerRedirect));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().ShouldContain("/identity/account/login");
    }

    [Fact]
    public async Task Sessions_for_different_tenants_coexist_in_one_browser()
    {
        var client = _host.CreateClient();
        await SignInAsync(client, "acme", "acme-spa", AcmeRedirect, "alice@acme.test", "Password1!");
        await SignInAsync(client, "consumer", "consumer-spa", ConsumerRedirect, "bob@consumer.test", "Password1!");

        var acme = await client.GetAsync(AuthorizeUrl("acme", "acme-spa", AcmeRedirect));
        var consumer = await client.GetAsync(AuthorizeUrl("consumer", "consumer-spa", ConsumerRedirect));

        acme.Headers.Location!.ToString().ShouldStartWith(AcmeRedirect);
        consumer.Headers.Location!.ToString().ShouldStartWith(ConsumerRedirect);
    }

    private static string AuthorizeUrl(string tenant, string clientId, string redirectUri) =>
        $"/{tenant}/connect/authorize?response_type=code&client_id={Uri.EscapeDataString(clientId)}" +
        $"&redirect_uri={Uri.EscapeDataString(redirectUri)}&scope=openid&code_challenge=x&code_challenge_method=plain&state=s";

    /// <summary>
    /// Runs GET authorize → GET login → POST credentials and returns the concatenated <c>Set-Cookie</c>
    /// header from the successful login POST.
    /// </summary>
    private static async Task<string> SignInAsync(
        HttpClient client, string tenant, string clientId, string redirectUri, string userName, string password)
    {
        var toLogin = await client.GetAsync(AuthorizeUrl(tenant, clientId, redirectUri));
        toLogin.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var loginUrl = toLogin.Headers.Location!.ToString();
        loginUrl.ShouldContain("/identity/account/login");

        var loginPage = await client.GetStringAsync(loginUrl);
        var antiforgery = AntiforgeryRegex().Match(loginPage).Groups["v"].Value;
        antiforgery.ShouldNotBeNullOrEmpty();

        var postLogin = await client.PostAsync(loginUrl, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = userName,
            ["Input.Password"] = password,
            ["Input.RememberMe"] = "false",
            ["__RequestVerificationToken"] = antiforgery,
        }));

        postLogin.StatusCode.ShouldBe(HttpStatusCode.Redirect, await postLogin.Content.ReadAsStringAsync());
        return postLogin.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? string.Join("\n", cookies)
            : string.Empty;
    }

    [GeneratedRegex("""name="__RequestVerificationToken"[^>]*value="(?<v>[^"]+)""")]
    private static partial Regex AntiforgeryRegex();
}
