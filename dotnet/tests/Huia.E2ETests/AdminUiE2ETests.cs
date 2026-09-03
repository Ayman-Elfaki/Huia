using Microsoft.Playwright;
using static Huia.E2ETests.FrontEndFlows;

namespace Huia.E2ETests;

/// <summary>
/// Browser-drives the <c>Huia.AdminUI</c> console end to end against the real <c>Huia.AppHost</c> stack
/// (Postgres + identity server + the Nuxt admin app), signing in through OpenIddict as the seeded
/// administrator. Covers every admin surface: the dashboard, the tenants / users / clients / keys lists,
/// and the full per-tenant scope CRUD, including that code-defined scopes are read-only.
/// </summary>
[Trait("Category", "E2E")]
[Collection("apphost")]
public sealed class AdminUiE2ETests(AppHostFixture host)
{
    private const string AdminEmail = "admin@huia.local";
    private const string AdminPassword = "Admin1!Pass";

    [SkippableFact]
    public async Task Signs_in_walks_every_section_and_signs_out()
    {
        Skip.IfNot(host.Started, host.SkipReason ?? "AppHost not started");
        await using var session = await BrowserSession.StartAsync();
        var page = session.Page;
        var console = CaptureConsole(page);

        try
        {
            await SignInAsync(page);

            await Expect(page.Locator("[data-testid=dashboard]")).ToBeVisibleAsync();
            await Expect(page.Locator("[data-testid=stat-tenants]")).ToBeVisibleAsync();

            // The sidebar links drive client-side navigation.
            await page.ClickAsync("[data-testid=nav-tenants]");
            await Expect(page.Locator("[data-testid=tenant-table]")).ToBeVisibleAsync(new() { Timeout = 15_000 });

            await page.ClickAsync("[data-testid=nav-users]");
            await Expect(page.Locator("[data-testid=user-table]")).ToBeVisibleAsync(new() { Timeout = 15_000 });
            await Expect(page.Locator("[data-testid=page-next]")).ToBeVisibleAsync();
            await Expect(page.Locator("[data-testid=page-prev]")).ToBeVisibleAsync();

            await page.ClickAsync("[data-testid=nav-clients]");
            await Expect(page.Locator("[data-testid=client-table]")).ToBeVisibleAsync(new() { Timeout = 15_000 });
            // The seeded admin console client is defined in code.
            await Expect(page.GetByTestId("client-huia-admin-ui")).ToContainTextAsync("static");

            await page.ClickAsync("[data-testid=nav-keys]");
            await Expect(page.Locator("[data-testid=key-table]")).ToBeVisibleAsync(new() { Timeout = 15_000 });

            await SignOutAsync(page, host.AdminAppUrl);
        }
        catch (Exception ex)
        {
            throw await DiagnoseAsync(page, "sections", console, ex);
        }
    }

    [SkippableFact]
    public async Task Creates_edits_and_deletes_a_scope_and_leaves_code_defined_scopes_locked()
    {
        Skip.IfNot(host.Started, host.SkipReason ?? "AppHost not started");
        await using var session = await BrowserSession.StartAsync();
        var page = session.Page;
        var console = CaptureConsole(page);

        try
        {
            await SignInAsync(page);

            await page.GotoAsync($"{host.AdminAppUrl}/scopes");
            await Expect(page.Locator("[data-testid=scope-table]")).ToBeVisibleAsync(new() { Timeout = 15_000 });
            await WaitForHydrationAsync(page);

            // Narrow to the tenant that carries the seeded (static) scope.
            await page.SelectOptionAsync("[data-testid=tenant-filter]", "todo");

            var staticRow = page.GetByTestId("scope-row-reports:read");
            await Expect(staticRow).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await Expect(staticRow).ToContainTextAsync("static");
            await Expect(page.GetByTestId("scope-edit-reports:read")).ToBeDisabledAsync();
            await Expect(page.GetByTestId("scope-delete-reports:read")).ToBeDisabledAsync();

            // Create a dynamic scope. Retry the click: the Nuxt dev server can still be hydrating.
            await ClickUntilVisibleAsync(page, "[data-testid=scope-create]", "[data-testid=scope-form]");
            await page.SelectOptionAsync("[data-testid=scope-tenant]", "todo");
            await page.FillAsync("[data-testid=scope-name]", "billing:read");
            await page.FillAsync("[data-testid=scope-display]", "Read billing");
            await page.FillAsync("[data-testid=scope-resources]", "billing-api");
            await page.ClickAsync("[data-testid=scope-submit]");

            var dynamicRow = page.GetByTestId("scope-row-billing:read");
            await Expect(dynamicRow).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await Expect(dynamicRow).ToContainTextAsync("dynamic");

            // Edit it (data-testids carry a ':' so address them with GetByTestId, not a CSS selector).
            await ClickUntilVisibleAsync(page, "[data-testid=\"scope-edit-billing:read\"]", "[data-testid=scope-form]");
            await page.FillAsync("[data-testid=scope-display]", "Read billing data");
            await page.ClickAsync("[data-testid=scope-submit]");
            await Expect(dynamicRow).ToContainTextAsync("Read billing data", new() { Timeout = 10_000 });

            // Delete it.
            await page.GetByTestId("scope-delete-billing:read").ClickAsync();
            await page.GetByTestId("scope-confirm-delete-billing:read").ClickAsync();
            await Expect(dynamicRow).ToHaveCountAsync(0, new() { Timeout = 10_000 });
        }
        catch (Exception ex)
        {
            throw await DiagnoseAsync(page, "scopes", console, ex);
        }
    }

    private async Task SignInAsync(IPage page)
    {
        await page.GotoAsync($"{host.AdminAppUrl}/", new() { Timeout = 60_000 });
        await Expect(page.Locator("[data-testid=landing-sign-in]")).ToBeVisibleAsync(new() { Timeout = 60_000 });

        // The Nuxt dev server can still be hydrating when the button first paints, so the click may
        // land before the handler is wired. Retry until the redirect to the identity server begins;
        // fall back to the nuxt-oidc-auth login route, which needs no client-side JS.
        static bool OnIdp(string u) =>
            u.Contains("/identity/account/login", StringComparison.Ordinal)
            || u.Contains("/connect/authorize", StringComparison.Ordinal);

        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (!OnIdp(page.Url))
        {
            if (DateTime.UtcNow > deadline)
            {
                await page.GotoAsync($"{host.AdminAppUrl}/auth/oidc/login", new() { Timeout = 30_000 });
                break;
            }

            try { await page.ClickAsync("[data-testid=landing-sign-in]", new() { Timeout = 5_000 }); }
            catch (PlaywrightException) { /* navigated away between the check and the click */ }

            try { await page.WaitForURLAsync(OnIdp, new() { Timeout = 8_000 }); }
            catch (TimeoutException) { await page.WaitForTimeoutAsync(2_000); }
        }

        await page.WaitForURLAsync(OnIdp, new() { Timeout = 30_000 });
        await FillPasswordAsync(page, AdminEmail, AdminPassword);
        await WaitForAppAsync(page, host.AdminAppUrl);
        await Expect(page.Locator("[data-testid=user-name]")).ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    /// <summary>Waits for the Nuxt client app to finish mounting on the current page (dev-server hydration is slow).</summary>
    private static async Task WaitForHydrationAsync(IPage page)
    {
        try
        {
            await page.WaitForFunctionAsync(
                "() => { const n = document.getElementById('__nuxt'); return !!n && (!!n.__vue_app__ || !!n.__vnode); }",
                options: new PageWaitForFunctionOptions { Timeout = 20_000 });
        }
        catch (TimeoutException)
        {
            // The retrying click below still tolerates a slow hydrate.
        }
    }

    /// <summary>Clicks <paramref name="clickSelector"/> and retries until <paramref name="expectSelector"/> is visible.</summary>
    private static async Task ClickUntilVisibleAsync(IPage page, string clickSelector, string expectSelector, int attempts = 6)
    {
        for (var i = 0; i < attempts; i++)
        {
            try { await page.ClickAsync(clickSelector, new() { Timeout = 10_000 }); }
            catch (PlaywrightException) { /* element churned mid-click; retry */ }

            try
            {
                await page.Locator(expectSelector).WaitForAsync(new() { Timeout = 5_000 });
                return;
            }
            catch (TimeoutException)
            {
                await page.WaitForTimeoutAsync(1_500);
            }
        }

        await Expect(page.Locator(expectSelector)).ToBeVisibleAsync(new() { Timeout = 5_000 });
    }

    private static List<string> CaptureConsole(IPage page)
    {
        var lines = new List<string>();
        page.Console += (_, m) => lines.Add($"{m.Type}: {m.Text}");
        page.PageError += (_, e) => lines.Add($"pageerror: {e}");
        return lines;
    }

    private static async Task<Exception> DiagnoseAsync(IPage page, string label, List<string> console, Exception inner)
    {
        var shot = Path.Combine(AppContext.BaseDirectory, $"admin-{label}-failure-{DateTime.UtcNow:HHmmss}.png");
        try { await page.ScreenshotAsync(new() { Path = shot, FullPage = true }); }
        catch { /* best effort */ }

        string body;
        try { body = await page.Locator("body").InnerHTMLAsync(); }
        catch { body = await page.ContentAsync(); }

        return new InvalidOperationException(
            $"admin-ui '{label}' spec failed at {page.Url}. Screenshot: {shot}\n"
            + $"--- console ---\n{string.Join("\n", console.TakeLast(40))}\n"
            + $"--- body ---\n{(body.Length > 6000 ? body[..6000] : body)}",
            inner);
    }
}
