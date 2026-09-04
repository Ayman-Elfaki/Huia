using System.Text.Json;
using Microsoft.Playwright;
using static Huia.E2ETests.FrontEndFlows;

namespace Huia.E2ETests;

/// <summary>
/// Drives the first-party <c>huia-nuxt</c> module end to end: its playground app against a real
/// <c>Huia.IdentityServer</c> (<c>e2e</c> tenant, PAR advertised, a 35s access-token lifetime).
/// Asserts the dual-layer session — tokens stay in Nitro Storage, the browser only ever sees the
/// sealed session cookie — plus transparent refresh and RP-initiated logout.
/// </summary>
[Trait("Category", "E2E")]
[Collection("huia-nuxt")]
public sealed class HuiaNuxtE2ETests(HuiaNuxtPlaygroundFixture fx)
{
    private const string UserEmail = "e2e-user@huia.local";
    private const string UserPassword = "Password1!";

    [SkippableFact]
    public async Task Signs_in_stores_tokens_server_side_and_serves_a_token_free_session()
    {
        Skip.IfNot(fx.Started, fx.SkipReason ?? "playground stack not started");
        await using var session = await BrowserSession.StartAsync();
        var page = session.Page;

        // A protected page bounces through the Huia authorize endpoint (PAR: request_uri only).
        await page.GotoAsync($"{fx.PlaygroundUrl}/protected");
        await page.WaitForURLAsync(u => u.StartsWith(fx.Issuer, StringComparison.Ordinal), new() { Timeout = 30_000 });

        await FillPasswordAsync(page, UserEmail, UserPassword);
        await WaitForAppAsync(page, fx.PlaygroundUrl);

        // The protected page rendered server-side with the seeded user's claims.
        await Expect(page.Locator("body")).ToContainTextAsync("e2e-user@huia.local", new() { Timeout = 15_000 });

        // The session cookie is present; NO cookie carries a token. (http origin ⇒ the __Host- prefix
        // is dropped, so the base name is `huia_sess`.)
        var cookies = await page.Context.CookiesAsync();
        Assert.Contains(cookies, c => c.Name is "huia_sess" or "huia_sess.0");
        Assert.DoesNotContain(cookies, c => c.Name.Contains("token", StringComparison.OrdinalIgnoreCase));

        // The session endpoint exposes claims but never a token.
        var body = await page.EvaluateAsync<string>(
            "async () => await (await fetch('/api/_auth/session')).text()");
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.GetProperty("loggedIn").GetBoolean());
        Assert.Equal("e2e-user@huia.local", doc.RootElement.GetProperty("user").GetProperty("name").GetString());
        Assert.DoesNotContain("accessToken", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("access_token", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refresh", body, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task Refreshes_the_access_token_transparently()
    {
        Skip.IfNot(fx.Started, fx.SkipReason ?? "playground stack not started");
        await using var session = await BrowserSession.StartAsync();
        var page = session.Page;

        await page.GotoAsync($"{fx.PlaygroundUrl}/protected");
        await page.WaitForURLAsync(u => u.StartsWith(fx.Issuer, StringComparison.Ordinal), new() { Timeout = 30_000 });
        await FillPasswordAsync(page, UserEmail, UserPassword);
        await WaitForAppAsync(page, fx.PlaygroundUrl);

        // The client's access token lives 35s and earlyRefreshSeconds is 60, so every /api/_auth/session
        // call refreshes against the real Huia token endpoint and pushes `expiresAt` forward.
        static async Task<long> ExpiresAt(IPage p)
        {
            var body = await p.EvaluateAsync<string>("async () => await (await fetch('/api/_auth/session')).text()");
            using var d = JsonDocument.Parse(body);
            return d.RootElement.GetProperty("expiresAt").GetInt64();
        }

        var first = await ExpiresAt(page);
        await Task.Delay(1500);
        var second = await ExpiresAt(page);

        Assert.True(second > first, $"expiresAt did not advance ({first} -> {second}); refresh not happening");

        // Still authenticated after the refreshes.
        await page.ReloadAsync();
        await Expect(page.Locator("body")).ToContainTextAsync("e2e-user@huia.local", new() { Timeout = 15_000 });
    }

    [SkippableFact]
    public async Task Sign_out_clears_the_session_and_reprotects_the_route()
    {
        Skip.IfNot(fx.Started, fx.SkipReason ?? "playground stack not started");
        await using var session = await BrowserSession.StartAsync();
        var page = session.Page;

        await page.GotoAsync($"{fx.PlaygroundUrl}/protected");
        await page.WaitForURLAsync(u => u.StartsWith(fx.Issuer, StringComparison.Ordinal), new() { Timeout = 30_000 });
        await FillPasswordAsync(page, UserEmail, UserPassword);
        await WaitForAppAsync(page, fx.PlaygroundUrl);

        await page.GotoAsync($"{fx.PlaygroundUrl}/auth/oidc/logout");
        await page.WaitForURLAsync(
            u => u.StartsWith(fx.PlaygroundUrl, StringComparison.Ordinal)
                 && !u.Contains("/auth/oidc/", StringComparison.Ordinal)
                 && !u.Contains("/connect/", StringComparison.Ordinal),
            new() { Timeout = 30_000 });

        var cookies = await page.Context.CookiesAsync();
        Assert.DoesNotContain(cookies, c => c.Name.StartsWith("huia_sess", StringComparison.Ordinal));

        // /protected is protected again.
        await page.GotoAsync($"{fx.PlaygroundUrl}/protected");
        await page.WaitForURLAsync(u => u.StartsWith(fx.Issuer, StringComparison.Ordinal), new() { Timeout = 30_000 });
    }
}
