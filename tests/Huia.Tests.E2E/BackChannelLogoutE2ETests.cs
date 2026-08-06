using Microsoft.Playwright;

namespace Huia.Tests.E2E;

/// <summary>
/// Proves TodoApp's back-channel logout receiver (<c>samples/Huia.TodoApp</c>'s
/// <c>/api/auth/backchannel-logout</c> route) works end to end against the real running Next.js app — unlike
/// <c>Huia.Tests.Integration</c>'s <c>LogoutNotificationTests</c>, which simulates the POST via a fake HTTP
/// interceptor rather than a real request. <c>ManageSessionsEndpoints.RevokeOthersAsync</c> notifies every
/// client tied to the target session, including <c>todo-web</c> itself (unlike <c>/connect/logout</c>, which
/// excludes whichever client initiated it) — so two "devices" signed into TodoApp with the same account
/// already exercise the full path without needing a second registered client: Huia signs a real
/// <c>logout_token</c>, POSTs it to the real Next.js route, which verifies it against Huia's JWKS (fetched
/// from its discovery document) and deletes the Redis-backed session it names.
/// </summary>
[Collection("Huia E2E")]
public sealed class BackChannelLogoutE2ETests(HuiaAppFixture fixture) : IAsyncLifetime
{
    private const string Password = "P@ssw0rd123!";

    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync();
    }

    public async Task DisposeAsync()
    {
        await _browser.DisposeAsync();
        _playwright.Dispose();
    }

    [Fact]
    public async Task RevokeOtherSessions_SignsOutTheOtherDevice_ViaRealBackChannelPost()
    {
        var webBaseUrl = GetBaseUrl("web");
        var email = $"e2e-backchannel-{Guid.NewGuid():N}@example.com";

        // Two separate browser contexts — separate cookie jars, i.e. separate "devices" — each ending up
        // with its own Huia session (sid) for the same account, the same way two separate browsers would.
        await using var contextA = await _browser.NewContextAsync(new() { IgnoreHTTPSErrors = true });
        var pageA = await contextA.NewPageAsync();
        await RegisterAndSignInAsync(pageA, webBaseUrl, email);

        await using var contextB = await _browser.NewContextAsync(new() { IgnoreHTTPSErrors = true });
        var pageB = await contextB.NewPageAsync();
        await SignInAsync(pageB, webBaseUrl, email);
        await Assertions.Expect(pageB.GetByText("E2E BackChannel")).ToBeVisibleAsync();

        // Device A's "sign out of all other sessions" is session-scoped-but-all-clients (see this class's own
        // doc comment) — with only todo-web involved here, it notifies todo-web's own backchannel-logout
        // route for device B's session.
        // The profile link is a Base UI <Button render={<Link .../>}> — an <a> under the hood, but Base UI
        // preserves button semantics (role="button") on whatever element `render` swaps in, so this queries
        // by button role rather than link role.
        await pageA.GetByRole(AriaRole.Button, new() { Name = "Profile" }).ClickAsync();
        await Assertions.Expect(pageA.GetByText("This device")).ToBeVisibleAsync();
        await pageA.GetByRole(AriaRole.Button, new() { Name = "Sign out of all other sessions" }).ClickAsync();

        // Proves Huia's own side of the revoke completed (the session-scoped React Server Action awaits
        // RevokeOthersAsync, which itself awaits the back-channel POST attempt before returning, then
        // revalidates this page) before blaming the real network hop to TodoApp's own route for whatever
        // device B still shows.
        await Assertions.Expect(pageA.GetByRole(AriaRole.Button, new() { Name = "Sign out of all other sessions" }))
            .Not.ToBeVisibleAsync();

        // Whether device B is signed in is decided server-side on each request (page.tsx's rawSession?.error
        // check), not reactively on the client, so this has to reload and re-check rather than wait on an
        // already-loaded page.
        // Generous: the first hit of a rarely-exercised Next.js dev-mode route (this one) pays an on-demand
        // compilation cost of several seconds on top of the real, sub-100ms POST once warm.
        await WaitForSignedOutAsync(pageB, webBaseUrl, timeoutMs: 45000);

        // Device A's own session is untouched — "sign out of all other sessions" excludes the caller.
        await pageA.GotoAsync(webBaseUrl);
        await Assertions.Expect(pageA.GetByText("E2E BackChannel")).ToBeVisibleAsync();
    }

    private static async Task RegisterAndSignInAsync(IPage page, string webBaseUrl, string email)
    {
        await page.GotoAsync(webBaseUrl);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "Create an account" }).ClickAsync();

        await page.GetByLabel("First name").FillAsync("E2E");
        await page.GetByLabel("Last name").FillAsync("BackChannel");
        await page.GetByLabel("Email").FillAsync(email);
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync(Password);
        await page.GetByLabel("Confirm password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Create account" }).ClickAsync();

        await page.WaitForURLAsync($"{webBaseUrl}*", new() { Timeout = 60000 });
    }

    private static async Task SignInAsync(IPage page, string webBaseUrl, string email)
    {
        await page.GotoAsync(webBaseUrl);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
        await Assertions.Expect(page.GetByLabel("Email")).ToBeVisibleAsync();

        await page.GetByLabel("Email").FillAsync(email);
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();

        await page.WaitForURLAsync($"{webBaseUrl}*", new() { Timeout = 60000 });
    }

    /// <summary>
    /// Reloads <paramref name="page"/> until the sign-in prompt appears (i.e. the server-side session lookup
    /// started returning a dead refresh token) or <paramref name="timeout"/> elapses — see the caller's own
    /// comment for why a single locator wait isn't enough here.
    /// </summary>
    private static async Task WaitForSignedOutAsync(IPage page, string webBaseUrl, int timeoutMs = 15000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (true)
        {
            await page.GotoAsync(webBaseUrl);
            var signInButton = page.GetByRole(AriaRole.Button, new() { Name = "Sign in" });

            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                await Assertions.Expect(signInButton).ToBeVisibleAsync();
                return;
            }

            try
            {
                await Assertions.Expect(signInButton)
                    .ToBeVisibleAsync(new() { Timeout = (float)Math.Min(remaining.TotalMilliseconds, 1000) });
                return;
            }
            catch (TimeoutException)
            {
                // Still signed in as of this reload — the back-channel POST hasn't landed yet; try again.
            }
        }
    }

    private string GetBaseUrl(string resourceName)
    {
        using var client = fixture.App.CreateHttpClient(resourceName);
        return client.BaseAddress!.ToString();
    }
}
