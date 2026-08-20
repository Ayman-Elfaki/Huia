using Microsoft.Playwright;

namespace Huia.Tests.E2E;


/// <summary>
/// Proves single sign-on across two different registered clients: once signed in to <c>todo-web</c>, visiting
/// a completely different client's own <c>/connect/authorize</c> request (here, the <c>scalar</c> client
/// Huia's own Scalar API reference uses) must silently reuse the live Huia session and hand back an
/// authorization code — not show Huia's login form again. Drives the raw authorize URL directly (the same one
/// Scalar's own "Authorize" button builds) rather than Scalar's third-party UI, so this stays stable across
/// Scalar version bumps.
/// </summary>
[Collection("Huia E2E")]
public sealed class SecondClientSsoE2ETests(HuiaAppFixture fixture) : IAsyncLifetime
{
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;
    private IPage _page = null!;

    public async Task InitializeAsync()
    {
        _playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync();
        _page = await _browser.NewPageAsync(new BrowserNewPageOptions { IgnoreHTTPSErrors = true });
    }

    public async Task DisposeAsync()
    {
        await _browser.DisposeAsync();
        _playwright.Dispose();
    }

    [Fact]
    public async Task Authorize_ForScalar_WhileSignedInToTodoWeb_SkipsLoginAndIssuesACode()
    {
        var webBaseUrl = GetBaseUrl("web");
        var apiBaseUrl = GetBaseUrl("todoapi");
        var email = $"e2e-sso-{Guid.NewGuid():N}@example.com";
        const string password = "P@ssw0rd123!";

        await _page.GotoAsync(webBaseUrl);
        await _page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
        await _page.GetByRole(AriaRole.Link, new() { Name = "Create an account" }).ClickAsync();

        await _page.GetByLabel("First name").FillAsync("EndToEnd");
        await _page.GetByLabel("Last name").FillAsync("Sso");
        await _page.GetByLabel("Email").FillAsync(email);
        await _page.GetByLabel("Password", new() { Exact = true }).FillAsync(password);
        await _page.GetByLabel("Confirm password").FillAsync(password);
        await _page.GetByRole(AriaRole.Button, new() { Name = "Create account" }).ClickAsync();

        await _page.WaitForURLAsync($"{webBaseUrl}*", new() { Timeout = 180000 });
        // The header shows the signed-in user's given/family name (from the OIDC profile claims), not
        // their raw email — see page.tsx.
        await Assertions.Expect(_page.GetByText("EndToEnd Sso")).ToBeVisibleAsync();

        var query = string.Join('&', new[]
        {
            "response_type=code",
            "client_id=scalar",
            $"redirect_uri={Uri.EscapeDataString($"{apiBaseUrl.TrimEnd('/')}/scalar/v1")}",
            "scope=todos",
            "code_challenge=abc",
            "code_challenge_method=plain",
        });

        await _page.GotoAsync($"{apiBaseUrl.TrimEnd('/')}/connect/authorize?{query}");

        // A silent SSO redirect lands back on /scalar/v1 with a code — a broken/reprompted flow would show
        // Huia's login form (an "Email" field) instead.
        await _page.WaitForURLAsync(
            url => url.Contains("/scalar/v1", StringComparison.Ordinal) && url.Contains("code=", StringComparison.Ordinal),
            new() { Timeout = 15000 });
    }

    private string GetBaseUrl(string resourceName)
    {
        using var client = fixture.App.CreateHttpClient(resourceName);
        return client.BaseAddress!.ToString();
    }
}
