using Microsoft.Playwright;

namespace Huia.Tests.E2E;

/// <summary>
/// Drives the real Huia.AdminUI sample end to end through a browser: sign in as the seeded demo admin
/// (see WebApplicationBuilderExtensions.SeedAdminAsync), confirm the dashboard is reachable, see the admin
/// account itself in the Users list, and create a Scope through the UI — a genuine write path through
/// nuxt-oidc-auth's session, the server-side admin-API proxy, and MapHuiaAdminEndpoints.
/// </summary>
[Collection("Huia E2E")]
public sealed class AdminUiE2ETests(HuiaAppFixture fixture) : IAsyncLifetime
{
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;
    private IPage _page = null!;

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync();

        // TodoApi runs on Aspire's local dev-cert HTTPS endpoint, which Node/.NET trust automatically (via
        // injected CA bundles) but a real browser does not.
        _page = await _browser.NewPageAsync(new BrowserNewPageOptions { IgnoreHTTPSErrors = true });
    }

    public async Task DisposeAsync()
    {
        await _browser.DisposeAsync();
        _playwright.Dispose();
    }

    [Fact]
    public async Task SignInAsAdmin_SeesSelfInUsersList_AndCreatesScope()
    {
        var adminBaseUrl = GetBaseUrl("admin-ui");

        await SignInAsAdminAsync(adminBaseUrl);

        // Landing on the dashboard redirects client-side to /applications (see app/pages/index.vue) —
        // this button only renders once that page, and the Admin-role gate in app.vue, both pass.
        await Assertions.Expect(_page.GetByRole(AriaRole.Button, new() { Name = "New application" }))
            .ToBeVisibleAsync(new() { Timeout = 120000 });

        // adminBaseUrl (from CreateHttpClient's BaseAddress) already ends with '/'.
        // The very first requests right after the OIDC callback sets nuxt-oidc-auth's session cookie can
        // 401 transiently (confirmed by instrumenting server/api/admin/[...path].ts directly: repeated
        // "Unauthorized" from requireUserSession for a few seconds, then it just starts working) — the cookie
        // itself needs a moment to become valid for requireUserSession's lookup. GotoWithRetryAsync reloads
        // past that window instead of asserting once and failing on it.
        // GetByRole(Cell, ...) rather than GetByText: the signed-in admin's own email also renders in the
        // sidebar's persistent account footer (app.vue) on every page, so a plain text locator matches both
        // that and the actual table row — scoping to the table cell's role is what the row is uniquely.
        await GotoWithRetryAsync($"{adminBaseUrl}users",
            _page.GetByRole(AriaRole.Cell, new() { Name = "admin@example.com" }));

        await GotoWithRetryAsync($"{adminBaseUrl}scopes", _page.GetByRole(AriaRole.Button, new() { Name = "New scope" }));
        var scopeName = $"e2e-scope-{Guid.NewGuid():N}";

        // The button is visible from SSR'd markup before Vue finishes hydrating and attaches its @click
        // handler — a click that lands in that window is inert (no navigation, no error, nothing happens).
        // Retrying the click until the dialog actually opens rides out that window instead of failing on it.
        var dialog = _page.GetByRole(AriaRole.Dialog);
        for (var attempt = 1; ; attempt++)
        {
            await _page.GetByRole(AriaRole.Button, new() { Name = "New scope" }).ClickAsync();
            try
            {
                await Assertions.Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
                break;
            }
            catch (PlaywrightException) when (attempt < 5)
            {
            }
        }
        await dialog.GetByLabel("Name", new() { Exact = true }).FillAsync(scopeName);
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        await Assertions.Expect(_page.GetByText(scopeName)).ToBeVisibleAsync();
    }

    private async Task SignInAsAdminAsync(string adminBaseUrl)
    {
        // The global nuxt-oidc-auth middleware redirects any unauthenticated page straight through
        // /auth/login -> /auth/oidc/login -> Huia's own /connect/authorize -> its login page — no "Sign in"
        // button to click first, unlike the Todo web app's own landing page.
        await _page.GotoAsync(adminBaseUrl);

        await _page.GetByLabel("Email").FillAsync("admin@example.com");
        await _page.GetByRole(AriaRole.Button, new() { Name = "Next" }).ClickAsync();
        await _page.GetByLabel("Password", new() { Exact = true }).FillAsync("Admin123!Demo");
        await _page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();

        await _page.WaitForURLAsync($"{adminBaseUrl}*", new() { Timeout = 180000 });
    }

    private async Task GotoWithRetryAsync(string url, ILocator expectVisible)
    {
        for (var attempt = 1; ; attempt++)
        {
            await _page.GotoAsync(url);
            try
            {
                await Assertions.Expect(expectVisible).ToBeVisibleAsync(new() { Timeout = 5000 });
                return;
            }
            catch (PlaywrightException) when (attempt < 5)
            {
            }
        }
    }

    private string GetBaseUrl(string resourceName)
    {
        using var client = fixture.App.CreateHttpClient(resourceName);
        return client.BaseAddress!.ToString();
    }
}
