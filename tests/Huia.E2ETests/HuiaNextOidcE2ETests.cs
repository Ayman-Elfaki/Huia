using System.Text.Json;
using Microsoft.Playwright;
using static Huia.E2ETests.FrontEndFlows;

namespace Huia.E2ETests;

/// <summary>
/// Drives the Next.js sample stack (<c>Todo.Next</c> via <c>next-huia-oidc</c>) against
/// <c>Todo.IdentityServer</c> and <c>Todo.Api</c>.
/// Asserts the dual-layer session (browser only receives sealed cookie, tokens held on server),
/// transparent session retrieval, authenticated API calls against Todo.Api, and RP logout.
/// </summary>
[Trait("Category", "E2E")]
[Collection("next-frontend")]
public sealed class HuiaNextOidcE2ETests(NextFrontEndFixture fx)
{
    private const string UserEmail = "alice@todo.test";
    private const string UserPassword = "Password1!2345";

    [Fact]
    public async Task Signs_in_stores_tokens_server_side_and_serves_a_token_free_session()
    {
        await using var session = await BrowserSession.StartAsync();
        var page = session.Page;

        await page.GotoAsync(fx.TodoNextUrl);
        await page.ClickAsync("[data-testid=landing-sign-in], [data-testid=sign-in]");
        await page.WaitForURLAsync(u => u.StartsWith(fx.Issuer, StringComparison.Ordinal), new() { Timeout = 30_000 });

        await FillPasswordAsync(page, UserEmail, UserPassword);
        await WaitForAppAsync(page, fx.TodoNextUrl);

        // Rendered with the signed-in user's claims.
        await Expect(page.Locator("[data-testid=user-name]")).ToContainTextAsync(UserEmail, new() { Timeout = 15_000 });

        // Session cookie present, no tokens in cookies.
        var cookies = await page.Context.CookiesAsync();
        Assert.Contains(cookies, c => c.Name is "huia_sess" or "huia_sess.0");
        Assert.DoesNotContain(cookies, c => c.Name.Contains("token", StringComparison.OrdinalIgnoreCase));

        // The session endpoint exposes claims but never a token.
        var body = await page.EvaluateAsync<string>(
            "async () => await (await fetch('/api/auth/session')).text()");
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.GetProperty("loggedIn").GetBoolean());
        Assert.Equal(UserEmail, doc.RootElement.GetProperty("user").GetProperty("email").GetString());
        Assert.DoesNotContain("accessToken", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("access_token", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refreshToken", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Performs_authenticated_crud_operations_against_todo_api()
    {
        await using var session = await BrowserSession.StartAsync();
        var page = session.Page;

        await page.GotoAsync(fx.TodoNextUrl);
        await page.ClickAsync("[data-testid=landing-sign-in], [data-testid=sign-in]");
        await page.WaitForURLAsync(u => u.StartsWith(fx.Issuer, StringComparison.Ordinal), new() { Timeout = 30_000 });
        await FillPasswordAsync(page, UserEmail, UserPassword);
        await WaitForAppAsync(page, fx.TodoNextUrl);

        var todoTitle = $"Test Next.js OIDC Task {Guid.NewGuid():N}";
        await page.GetByPlaceholder("What needs to be done?").FillAsync(todoTitle);
        await page.GetByRole(AriaRole.Button, new() { Name = "Add" }).ClickAsync();

        await Expect(page.Locator("body")).ToContainTextAsync(todoTitle, new() { Timeout = 15_000 });

        // Sign out
        await SignOutAsync(page, fx.TodoNextUrl);
    }

    [Fact]
    public async Task Sign_out_clears_the_session()
    {
        await using var session = await BrowserSession.StartAsync();
        var page = session.Page;

        await page.GotoAsync(fx.TodoNextUrl);
        await page.ClickAsync("[data-testid=landing-sign-in], [data-testid=sign-in]");
        await page.WaitForURLAsync(u => u.StartsWith(fx.Issuer, StringComparison.Ordinal), new() { Timeout = 30_000 });
        await FillPasswordAsync(page, UserEmail, UserPassword);
        await WaitForAppAsync(page, fx.TodoNextUrl);

        await SignOutAsync(page, fx.TodoNextUrl);

        await page.GotoAsync(fx.TodoNextUrl);
        await Expect(page.Locator("[data-testid=landing-sign-in], [data-testid=sign-in]").First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(page.Locator("[data-testid=user-name]")).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task Signs_in_through_the_external_partner_provider()
    {
        await using var session = await BrowserSession.StartAsync();
        var page = session.Page;

        await page.GotoAsync(fx.TodoNextUrl);
        await page.ClickAsync("[data-testid=landing-sign-in], [data-testid=sign-in]");

        await page.WaitForSelectorAsync("[data-testid=external-providers]", new() { Timeout = 15_000 });
        await page.ClickAsync("[data-testid=external-providers] button");

        // Now on Huia.External's own Razor login page.
        await page.WaitForURLAsync(u => u.Contains("/partners/identity/account/", StringComparison.Ordinal), new() { Timeout = 25_000 });
        await FillPasswordAsync(page, "full@partners.test", "Partner1!Pass");

        if (await page.Locator("[data-testid=complete-profile-form]").IsVisibleAsync())
        {
            await page.ClickAsync("[data-testid=complete-profile-submit]");
        }

        await WaitForAppAsync(page, fx.TodoNextUrl);
        await Expect(page.Locator("[data-testid=user-name]")).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }
}

