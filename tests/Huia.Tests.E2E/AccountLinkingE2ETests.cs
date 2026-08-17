using Microsoft.Playwright;

namespace Huia.Tests.E2E;

/// <summary>
/// Drives Huia's password-confirmed account-linking shortcut end to end in a real browser:
/// <c>ext.EnablePasswordLinking()</c> (enabled unconditionally by <c>Huia.TodoApi/Program.cs</c>) means an
/// external sign-in via the self-hosted <c>identityserver</c> sample (see
/// <see cref="ExternalIdentityServerLoginE2ETests"/>) whose reported email collides with an existing password
/// account must route to <c>ExternalLoginLinkConfirmation</c> rather than silently auto-linking, creating a
/// second account, or (the no-opt-in default) just bouncing back to Login with an error — and only actually
/// link once the colliding account's real password is proven, not before.
/// </summary>
[Collection("Huia E2E")]
public sealed class AccountLinkingE2ETests(HuiaAppFixture fixture) : IAsyncLifetime
{
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;
    private IPage _page = null!;

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync();
        _page = await _browser.NewPageAsync(new BrowserNewPageOptions { IgnoreHTTPSErrors = true });
    }

    public async Task DisposeAsync()
    {
        await _browser.DisposeAsync();
        _playwright.Dispose();
    }

    // One line over MA0051's default 60-line limit; splitting this single linear browser script wouldn't
    // reduce its actual complexity, just spread it across an extra method.
#pragma warning disable MA0051
    [Fact]
    public async Task ExternalLogin_WithCollidingEmail_RequiresPasswordConfirmationThenLinksAndCompletes()
    {
        var webBaseUrl = GetBaseUrl("web");
        var apiBaseUrl = GetBaseUrl("todoapi").TrimEnd('/');
        var identityServerBaseUrl = GetBaseUrl("identityserver").TrimEnd('/');
        var email = $"e2e-link-{Guid.NewGuid():N}@example.com";
        const string password = "P@ssw0rd123!";

        // The account ext.EnablePasswordLinking() should link into — a normal TodoApi password account,
        // registered (and then signed out of) through web exactly like every other test in this suite.
        await RegisterAsync(webBaseUrl, email, password);
        await _page.GetByRole(AriaRole.Button, new() { Name = "Sign out" }).ClickAsync();
        await _page.WaitForURLAsync($"{webBaseUrl}*", new() { Timeout = 30000 });
        await Assertions.Expect(_page.GetByRole(AriaRole.Button, new() { Name = "Sign in" })).ToBeVisibleAsync();

        // Same pending-authorization setup as ExternalIdentityServerLoginE2ETests, so the end of this test can
        // prove the whole chain completed by resuming it to a real code, not just that some page loaded.
        var query = string.Join('&', new[]
        {
            "response_type=code",
            "client_id=scalar",
            $"redirect_uri={Uri.EscapeDataString($"{apiBaseUrl}/scalar/v1")}",
            "scope=todos",
            "code_challenge=abc",
            "code_challenge_method=plain",
        });

        await _page.GotoAsync($"{apiBaseUrl}/connect/authorize?{query}");
        await _page.WaitForURLAsync(url => url.Contains("/identity/account/login", StringComparison.Ordinal),
            new() { Timeout = 30000 });

        var idpButton = _page.GetByRole(AriaRole.Button, new() { Name = "Huia IdP", Exact = true });
        await Assertions.Expect(idpButton).ToBeVisibleAsync(new() { Timeout = 30000 });

        await Task.WhenAll(
            _page.WaitForURLAsync(
                url => string.Equals(new Uri(url).Host, new Uri(identityServerBaseUrl).Host, StringComparison.Ordinal)
                       && url.Contains("/identity/account/login", StringComparison.Ordinal),
                new() { Timeout = 30000 }),
            idpButton.ClickAsync());

        // A brand-new account on identityserver (a completely separate Huia instance/database) — but reusing
        // the exact same email as the TodoApi password account above is the whole point: that's what makes
        // this a colliding identity rather than a first-time external sign-in. Its own password here is
        // otherwise irrelevant — never used again once the round trip returns to TodoApi.
        await _page.GetByRole(AriaRole.Link, new() { Name = "Create an account" }).ClickAsync();

        await _page.GetByLabel("First name").FillAsync("E2E");
        await _page.GetByLabel("Last name").FillAsync("Link");
        await _page.GetByLabel("Email").FillAsync(email);
        await _page.GetByLabel("Password", new() { Exact = true }).FillAsync("Unrelat3dP@ssw0rd!");
        await _page.GetByLabel("Confirm password").FillAsync("Unrelat3dP@ssw0rd!");

        await Task.WhenAll(
            _page.WaitForURLAsync(
                url => string.Equals(new Uri(url).Host, new Uri(apiBaseUrl).Host, StringComparison.Ordinal),
                new() { Timeout = 30000 }),
            _page.GetByRole(AriaRole.Button, new() { Name = "Create account" }).ClickAsync());

        // ext.EnablePasswordLinking() routes the collision here instead of ExternalLoginConfirmation (which
        // would mean a second, separate account) or straight back to Login with just an error message (the
        // no-opt-in default) — proves the opt-in is actually wired up, not just documented.
        await _page.WaitForURLAsync(
            url => url.Contains("/identity/account/externalloginlinkconfirmation", StringComparison.OrdinalIgnoreCase),
            new() { Timeout = 30000 });

        // The colliding email and the actual provider display name render as read-only context pulled from
        // the pending external identity's own claims, not from anything a user could tamper with.
        await Assertions.Expect(_page.GetByText(email)).ToBeVisibleAsync();
        await Assertions.Expect(_page.GetByText("Huia IdP")).ToBeVisibleAsync();

        // Wrong password first: proves ExternalLoginLinkConfirmationModel actually checks the TodoApi
        // account's own password rather than linking on the strength of the external identity alone, and that
        // a failed attempt leaves the pending external identity live for a retry instead of discarding it.
        await _page.GetByLabel("Password").FillAsync("WrongPassword!1");
        await _page.GetByRole(AriaRole.Button, new() { Name = "Link account" }).ClickAsync();

        await Assertions.Expect(_page.GetByText("Incorrect password.")).ToBeVisibleAsync(new() { Timeout = 30000 });
        Assert.Contains("/identity/account/externalloginlinkconfirmation", _page.Url,
            StringComparison.OrdinalIgnoreCase);

        await _page.GetByLabel("Password").FillAsync(password);
        await _page.GetByRole(AriaRole.Button, new() { Name = "Link account" }).ClickAsync();

        // Resumes the original "scalar" authorization request straight through to a code — proof the external
        // identity actually got linked to the pre-existing password account and signed in as it, not just
        // that the form accepted the password.
        await _page.WaitForURLAsync(
            url => url.Contains("/scalar/v1", StringComparison.Ordinal) && url.Contains("code=", StringComparison.Ordinal),
            new() { Timeout = 30000 });
    }
#pragma warning restore MA0051

    private async Task RegisterAsync(string webBaseUrl, string email, string password)
    {
        await _page.GotoAsync(webBaseUrl);
        await _page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
        await _page.GetByRole(AriaRole.Link, new() { Name = "Create an account" }).ClickAsync();

        await _page.GetByLabel("First name").FillAsync("E2E");
        await _page.GetByLabel("Last name").FillAsync("Link");
        await _page.GetByLabel("Email").FillAsync(email);
        await _page.GetByLabel("Password", new() { Exact = true }).FillAsync(password);
        await _page.GetByLabel("Confirm password").FillAsync(password);
        await _page.GetByRole(AriaRole.Button, new() { Name = "Create account" }).ClickAsync();

        await _page.WaitForURLAsync($"{webBaseUrl}*", new() { Timeout = 60000 });
        await Assertions.Expect(_page.GetByText("E2E Link")).ToBeVisibleAsync();
    }

    private string GetBaseUrl(string resourceName)
    {
        using var client = fixture.App.CreateHttpClient(resourceName);
        return client.BaseAddress!.ToString();
    }
}
