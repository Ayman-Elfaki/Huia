using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Huia.E2ETests;

/// <summary>
/// End-to-end specs for external login (partner IdP federation) running against the full
/// <c>Huia.AppHost</c> stack booted via <c>Aspire.Hosting.Testing</c>.
///
/// Verifies that both <c>Todo.IdentityServer</c> (OpenIddict client) and <c>Shop.Api</c>
/// (Huia.Headless OpenIdConnect handler) successfully discover, challenge, authenticate, and
/// exchange tokens with <c>Huia.External</c> in the Aspire environment.
/// </summary>
[Trait("Category", "E2E")]
[Collection("apphost")]
public sealed class AppHostExternalLoginE2ETests(AppHostFixture host)
{
    [SkippableFact]
    public async Task External_login_via_Aspire_succeeds_for_identity_server()
    {
        Skip.IfNot(host.Started, host.SkipReason ?? "AppHost not started");

        await using var session = await BrowserSession.StartAsync();
        var page = session.Page;

        // Navigate to the todo tenant login page on IdentityServer
        await page.GotoAsync($"{host.Issuer}/todo/identity/account/login?returnUrl=%2Ftodo%2F");

        await page.WaitForSelectorAsync("[data-testid=external-providers] button", new() { Timeout = 25_000 });
        await page.ClickAsync("[data-testid=external-providers] button");

        try
        {
            await page.WaitForURLAsync(u => u.Contains("/partners/identity/account/", StringComparison.Ordinal), new() { Timeout = 10_000 });
        }
        catch (TimeoutException)
        {
            await page.EvalOnSelectorAsync("[data-testid=external-providers] form", "f => f.requestSubmit()");
            await page.WaitForURLAsync(u => u.Contains("/partners/identity/account/", StringComparison.Ordinal), new() { Timeout = 25_000 });
        }
        await FrontEndFlows.FillPasswordAsync(page, "full@partners.test", "Partner1!Pass");

        // If complete-profile form is shown, submit it
        if (await page.Locator("[data-testid=complete-profile-form]").IsVisibleAsync())
        {
            await page.ClickAsync("[data-testid=complete-profile-submit]");
        }

        // Successfully redirected back to IdentityServer with active session
        await page.WaitForURLAsync(u => u.Contains("/todo/", StringComparison.Ordinal) || u.StartsWith(host.Issuer, StringComparison.Ordinal), new() { Timeout = 25_000 });
    }

    [SkippableFact]
    public async Task External_login_via_Aspire_succeeds_for_shop_api()
    {
        Skip.IfNot(host.Started, host.SkipReason ?? "AppHost not started");

        await using var session = await BrowserSession.StartAsync();
        var page = session.Page;

        // Initiate external login on Shop.Api
        var returnUrl = $"{host.ShopApiUrl}/callback";
        var challengeUrl = $"{host.ShopApiUrl}/identity/account/external/huia?returnUrl={Uri.EscapeDataString(returnUrl)}";

        await page.GotoAsync(challengeUrl);

        // Browser redirected to Huia.External login page
        await page.WaitForURLAsync(u => u.Contains("/partners/identity/account/", StringComparison.Ordinal), new() { Timeout = 25_000 });
        await FrontEndFlows.FillPasswordAsync(page, "full@partners.test", "Partner1!Pass");

        // Expect redirect to returnUrl with code query param
        await page.WaitForURLAsync(u => u.Contains("/callback") && u.Contains("code="), new() { Timeout = 25_000 });

        var callbackUri = new Uri(page.Url);
        var queryParams = System.Web.HttpUtility.ParseQueryString(callbackUri.Query);
        var code = queryParams["code"];
        code.ShouldNotBeNullOrEmpty();

        // Verify the code can be exchanged against Shop.Api
        using var client = host.CreateClient(host.ShopApiUrl);
        var exchangeResponse = await client.PostAsJsonAsync("identity/account/external/exchange", new { code });
        ((int)exchangeResponse.StatusCode).ShouldBeInRange(200, 299);
    }

    [SkippableFact]
    public async Task External_login_via_Aspire_succeeds_from_todo_app()
    {
        Skip.IfNot(host.Started, host.SkipReason ?? "AppHost not started");

        await using var session = await BrowserSession.StartAsync();
        var page = session.Page;

        var todoUrl = !string.IsNullOrEmpty(host.TodoAppUrl) ? host.TodoAppUrl : "http://localhost:3000";
        // Navigate directly to the OIDC login endpoint which starts the authorization flow
        await page.GotoAsync($"{todoUrl}/auth/oidc/login", new() { Timeout = 30_000 });

        // IdentityServer login page
        await page.WaitForURLAsync(u => u.Contains("/identity/account/login", StringComparison.Ordinal) || u.Contains("/connect/authorize", StringComparison.Ordinal), new() { Timeout = 25_000 });
        await page.WaitForSelectorAsync("[data-testid=external-providers] button", new() { Timeout = 25_000 });
        await page.ClickAsync("[data-testid=external-providers] button");

        try
        {
            await page.WaitForURLAsync(u => u.Contains("/partners/identity/account/", StringComparison.Ordinal), new() { Timeout = 10_000 });
        }
        catch (TimeoutException)
        {
            await page.EvalOnSelectorAsync("[data-testid=external-providers] form", "f => f.requestSubmit()");
            await page.WaitForURLAsync(u => u.Contains("/partners/identity/account/", StringComparison.Ordinal), new() { Timeout = 25_000 });
        }

        await FrontEndFlows.FillPasswordAsync(page, "full@partners.test", "Partner1!Pass");

        if (await page.Locator("[data-testid=complete-profile-form]").IsVisibleAsync())
        {
            await page.ClickAsync("[data-testid=complete-profile-submit]");
        }

        // Successfully redirected back to Todo.Nuxt and signed in
        await page.WaitForURLAsync(u => u.TrimEnd('/') == todoUrl.TrimEnd('/') || u.Contains("localhost:3000"), new() { Timeout = 25_000 });
        await Expect(page.Locator("[data-testid=user-name]")).ToBeVisibleAsync(new() { Timeout = 25_000 });
    }

    [SkippableFact]
    public async Task External_login_via_Aspire_succeeds_from_shop_app()
    {
        Skip.IfNot(host.Started, host.SkipReason ?? "AppHost not started");

        await using var session = await BrowserSession.StartAsync();
        var page = session.Page;

        var shopUrl = !string.IsNullOrEmpty(host.ShopAppUrl) ? host.ShopAppUrl : "http://localhost:3002";
        await page.GotoAsync($"{shopUrl}/login");

        await page.WaitForSelectorAsync("text=Sign in with Partner", new() { Timeout = 30_000 });
        await page.GetByRole(AriaRole.Link, new() { Name = "Sign in with Partner" }).ClickAsync();

        // Browser redirected to Huia.External login page
        await page.WaitForURLAsync(u => u.Contains("/partners/identity/account/", StringComparison.Ordinal), new() { Timeout = 25_000 });
        await FrontEndFlows.FillPasswordAsync(page, "full@partners.test", "Partner1!Pass");

        // First time this partner signs in to Shop.Api, it shows the name confirmation form
        await Expect(page.Locator("body")).ToContainTextAsync("what's your name", new() { Timeout = 25_000 });
        await page.GetByRole(AriaRole.Button, new() { Name = "Finish" }).ClickAsync();

        await page.WaitForURLAsync(u => u.TrimEnd('/') == shopUrl.TrimEnd('/'), new() { Timeout = 15_000 });
        await Expect(page.Locator("header")).ToContainTextAsync("full@partners.test", new() { Timeout = 15_000 });
    }

    [SkippableFact]
    public async Task External_login_via_Aspire_succeeds_from_todo_next()
    {
        Skip.IfNot(host.Started, host.SkipReason ?? "AppHost not started");

        await using var session = await BrowserSession.StartAsync();
        var page = session.Page;

        var todoNextUrl = !string.IsNullOrEmpty(host.TodoNextUrl) ? host.TodoNextUrl : "http://todo-next.dev.localhost:3050";
        await page.GotoAsync(todoNextUrl);
        await page.ClickAsync("[data-testid=landing-sign-in], [data-testid=sign-in]");

        await page.WaitForSelectorAsync("[data-testid=external-providers] button", new() { Timeout = 25_000 });
        await page.ClickAsync("[data-testid=external-providers] button");

        try
        {
            await page.WaitForURLAsync(u => u.Contains("/partners/identity/account/", StringComparison.Ordinal), new() { Timeout = 10_000 });
        }
        catch (TimeoutException)
        {
            await page.EvalOnSelectorAsync("[data-testid=external-providers] form", "f => f.requestSubmit()");
            await page.WaitForURLAsync(u => u.Contains("/partners/identity/account/", StringComparison.Ordinal), new() { Timeout = 25_000 });
        }

        await FrontEndFlows.FillPasswordAsync(page, "full@partners.test", "Partner1!Pass");

        if (await page.Locator("[data-testid=complete-profile-form]").IsVisibleAsync())
        {
            await page.ClickAsync("[data-testid=complete-profile-submit]");
        }

        await FrontEndFlows.WaitForAppAsync(page, todoNextUrl);
        await Expect(page.Locator("[data-testid=user-name]")).ToBeVisibleAsync(new() { Timeout = 25_000 });
    }

    [SkippableFact]
    public async Task External_login_via_Aspire_succeeds_from_shop_next()
    {
        Skip.IfNot(host.Started, host.SkipReason ?? "AppHost not started");

        await using var session = await BrowserSession.StartAsync();
        var page = session.Page;

        var shopNextUrl = !string.IsNullOrEmpty(host.ShopNextUrl) ? host.ShopNextUrl : "http://localhost:3060";
        await page.GotoAsync($"{shopNextUrl}/login");

        await page.WaitForSelectorAsync("text=Sign in with Partner", new() { Timeout = 30_000 });
        await page.GetByRole(AriaRole.Link, new() { Name = "Sign in with Partner" }).ClickAsync();

        // Browser redirected to Huia.External login page
        await page.WaitForURLAsync(u => u.Contains("/partners/identity/account/", StringComparison.Ordinal), new() { Timeout = 25_000 });
        await FrontEndFlows.FillPasswordAsync(page, "full@partners.test", "Partner1!Pass");

        // If this partner user was not previously provisioned, complete the name form
        if (await page.Locator("[data-testid=finish-profile-btn], button:has-text('Finish')").IsVisibleAsync())
        {
            await page.GetByRole(AriaRole.Button, new() { Name = "Finish" }).ClickAsync();
        }

        await page.WaitForURLAsync(u => u.TrimEnd('/') == shopNextUrl.TrimEnd('/'), new() { Timeout = 25_000 });
        await Expect(page.Locator("header")).ToContainTextAsync("Fiona Full", new() { Timeout = 25_000 });
    }
}
