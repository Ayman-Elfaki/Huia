using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Playwright;

namespace Huia.Tests.E2E;

/// <summary>
/// Enrolls a real account in TOTP two-factor authentication through the TodoApp's own profile page (see
/// <c>two-factor-panel.tsx</c>), then signs out and back in — since <c>PasswordSignInAsync</c> now reports
/// <c>RequiresTwoFactor</c>, Huia's login page should redirect to <c>LoginWith2fa.cshtml</c> for a second,
/// authenticator-code step instead of completing sign-in straight away (see <c>Login.cshtml.cs</c>).
/// </summary>
[Collection("Huia E2E")]
public sealed class LoginWith2faE2ETests(HuiaAppFixture fixture) : IAsyncLifetime
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
    public async Task SignIn_WithTwoFactorEnabled_RequiresAuthenticatorCode()
    {
        var webBaseUrl = GetBaseUrl("web");
        var email = $"e2e-2fa-{Guid.NewGuid():N}@example.com";
        const string password = "P@ssw0rd123!";

        await RegisterAsync(webBaseUrl, email, password);

        var sharedKey = await EnableTwoFactorAsync(webBaseUrl);

        // The sign-out button only lives in the home page's own header (see page.tsx) — the profile page has
        // none — so it takes a real navigation back before it's reachable.
        await _page.GotoAsync(webBaseUrl);

        // Full RP-initiated sign-out (see SignInCycleE2ETests), so the next "Sign in" click shows a real
        // login form rather than silently re-authenticating via a still-live IdP session.
        await _page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign out" }).ClickAsync();
        await _page.WaitForURLAsync($"{webBaseUrl}*", new PageWaitForURLOptions { Timeout = 120000 });
        await Assertions.Expect(_page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }))
            .ToBeVisibleAsync();

        await _page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await Assertions.Expect(_page.GetByLabel("Email")).ToBeVisibleAsync();

        await _page.GetByLabel("Email").FillAsync(email);
        await _page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Next" }).ClickAsync();
        await _page.GetByLabel("Password", new PageGetByLabelOptions { Exact = true }).FillAsync(password);
        await _page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();

        // The password check succeeds but this account now has 2FA enabled, so LoginModel.OnPostAsync redirects
        // to LoginWith2fa instead of completing sign-in — a real second-step form, not the app itself.
        await Assertions.Expect(_page.GetByLabel("Authenticator code")).ToBeVisibleAsync(new() { Timeout = 120000 });

        await _page.GetByLabel("Authenticator code").FillAsync(GenerateTotpCode(sharedKey));
        await _page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Verify" }).ClickAsync();

        await _page.WaitForURLAsync($"{webBaseUrl}*", new PageWaitForURLOptions { Timeout = 180000 });
        await Assertions.Expect(_page.GetByText("EndToEnd TwoFactor")).ToBeVisibleAsync();
    }

    private async Task RegisterAsync(string webBaseUrl, string email, string password)
    {
        await _page.GotoAsync(webBaseUrl);
        await _page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await _page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Create an account" }).ClickAsync();

        await _page.GetByLabel("First name").FillAsync("EndToEnd");
        await _page.GetByLabel("Last name").FillAsync("TwoFactor");
        await _page.GetByLabel("Email").FillAsync(email);
        await _page.GetByLabel("Password", new PageGetByLabelOptions { Exact = true }).FillAsync(password);
        await _page.GetByLabel("Confirm password").FillAsync(password);
        await _page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Create account" }).ClickAsync();

        await _page.WaitForURLAsync($"{webBaseUrl}*", new PageWaitForURLOptions { Timeout = 180000 });
        await Assertions.Expect(_page.GetByText("EndToEnd TwoFactor")).ToBeVisibleAsync();
    }

    /// <summary>Drives the profile page's own 2FA panel and returns the shared key it enrolled with.</summary>
    private async Task<string> EnableTwoFactorAsync(string webBaseUrl)
    {
        // Base UI's Button primitive sets role="button" explicitly even when `render` swaps in an <a> (see
        // button.tsx), so this is still an accessibility-tree button despite being a real anchor underneath.
        // It's a Next.js client-side route transition rather than a full page load, so waiting on navigation
        // "Load" here would hang until timeout — waiting for the shared key below to appear is what actually
        // signals the profile page has rendered.
        await _page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Profile" }).ClickAsync();

        // getTwoFactorStatus (the profile page's own server-side loader) deliberately stays a pure read and
        // never generates a shared key on its own — see its doc comment in manage-api.ts. The panel starts
        // in a third, pre-setup state (two-factor-panel.tsx) prompting to begin enrollment explicitly; only
        // that submission (intent: "start-setup") actually calls setTwoFactor(..., { resetSharedKey: true })
        // and gets one back.
        await _page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Set up two-factor authentication" })
            .ClickAsync();

        // The only <code> element on the profile page once the shared key comes back from that submission.
        var sharedKeyLocator = _page.Locator("code");
        await Assertions.Expect(sharedKeyLocator).ToBeVisibleAsync(new() { Timeout = 120000 });
        var sharedKey = (await sharedKeyLocator.InnerTextAsync()).Replace(" ", string.Empty);

        await _page.GetByLabel("6-digit code").FillAsync(GenerateTotpCode(sharedKey));
        await _page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Enable two-factor authentication" })
            .ClickAsync();

        await Assertions.Expect(_page.GetByText("Enabled", new PageGetByTextOptions { Exact = true }))
            .ToBeVisibleAsync();

        return sharedKey;
    }

    /// <summary>
    /// A standalone RFC 6238 TOTP implementation (SHA-1, 6 digits, 30s step) matching the algorithm behind
    /// ASP.NET Core Identity's own <c>AuthenticatorTokenProvider</c> — there's no test-only shortcut into that
    /// internal type, and no TOTP package is otherwise used anywhere in this solution.
    /// </summary>
    private static string GenerateTotpCode(string base32Secret)
    {
        var key = Base32Decode(base32Secret);
        var counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;

        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(counterBytes);
        }

        var hash = HMACSHA1.HashData(key, counterBytes);
        var offset = hash[^1] & 0x0F;
        var binaryCode = ((hash[offset] & 0x7F) << 24)
                          | ((hash[offset + 1] & 0xFF) << 16)
                          | ((hash[offset + 2] & 0xFF) << 8)
                          | (hash[offset + 3] & 0xFF);

        return (binaryCode % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    private static byte[] Base32Decode(string base32)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        var bytes = new List<byte>();
        var bitBuffer = 0;
        var bitCount = 0;

        foreach (var c in base32.TrimEnd('=').ToUpperInvariant())
        {
            var value = alphabet.IndexOf(c);
            if (value < 0)
            {
                continue;
            }

            bitBuffer = (bitBuffer << 5) | value;
            bitCount += 5;

            if (bitCount >= 8)
            {
                bitCount -= 8;
                bytes.Add((byte)((bitBuffer >> bitCount) & 0xFF));
            }
        }

        return bytes.ToArray();
    }

    private string GetBaseUrl(string resourceName)
    {
        using var client = fixture.App.CreateHttpClient(resourceName);
        return client.BaseAddress!.ToString();
    }
}
