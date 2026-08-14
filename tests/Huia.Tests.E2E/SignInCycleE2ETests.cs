using Microsoft.Playwright;

namespace Huia.Tests.E2E;

/// <summary>
/// Drives sign in, sign out, then sign in again through a real browser — the sign-out button performs a full
/// RP-initiated logout (see <c>huiaEndSessionUrl</c> in <c>auth.ts</c>), clearing both the Next.js app's own
/// session and Huia's own sign-in cookie, so the second sign-in should show — and successfully complete — a
/// real login form rather than silently failing or looping
/// </summary>
[Collection("Huia E2E")]
public sealed class SignInCycleE2ETests(HuiaAppFixture fixture) : IAsyncLifetime
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

    [Fact]
    public async Task SignOut_ThenSignInAgain_WithTheSameAccount_Succeeds()
    {
        var webBaseUrl = GetBaseUrl("web");
        var email = $"e2e-cycle-{Guid.NewGuid():N}@example.com";
        const string password = "P@ssw0rd123!";

        await _page.GotoAsync(webBaseUrl);
        await _page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await _page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Create an account" }).ClickAsync();

        await _page.GetByLabel("First name").FillAsync("E2E");
        await _page.GetByLabel("Last name").FillAsync("Cycle");
        await _page.GetByLabel("Email").FillAsync(email);
        await _page.GetByLabel("Password", new PageGetByLabelOptions { Exact = true }).FillAsync(password);
        await _page.GetByLabel("Confirm password").FillAsync(password);
        await _page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Create account" }).ClickAsync();

        await _page.WaitForURLAsync($"{webBaseUrl}*", new PageWaitForURLOptions { Timeout = 60000 });
        // The header shows the signed-in user's given/family name (from the OIDC profile claims), not
        // their raw email — see page.tsx.
        await Assertions.Expect(_page.GetByText("E2E Cycle")).ToBeVisibleAsync();

        await _page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign out" }).ClickAsync();
        await _page.WaitForURLAsync($"{webBaseUrl}*", new PageWaitForURLOptions { Timeout = 30000 });
        await Assertions.Expect(_page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }))
            .ToBeVisibleAsync();

        // Huia's own session was cleared too (full RP-initiated logout), so this must show a real login form
        // rather than silently re-authenticating via a still-live IdP session.
        await _page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await Assertions.Expect(_page.GetByLabel("Email")).ToBeVisibleAsync();

        await _page.GetByLabel("Email").FillAsync(email);
        await _page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Next" }).ClickAsync();
        await _page.GetByLabel("Password", new PageGetByLabelOptions { Exact = true }).FillAsync(password);
        await _page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();

        await _page.WaitForURLAsync($"{webBaseUrl}*", new PageWaitForURLOptions { Timeout = 60000 });
        // The header shows the signed-in user's given/family name (from the OIDC profile claims), not
        // their raw email — see page.tsx.
        await Assertions.Expect(_page.GetByText("E2E Cycle")).ToBeVisibleAsync();
    }

    private string GetBaseUrl(string resourceName)
    {
        using var client = fixture.App.CreateHttpClient(resourceName);
        return client.BaseAddress!.ToString();
    }
}