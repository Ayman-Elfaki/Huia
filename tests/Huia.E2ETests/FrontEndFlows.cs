using Microsoft.Playwright;

namespace Huia.E2ETests;

/// <summary>
/// Playwright helpers shared by the front-end sign-in specs (Todo.App on the out-of-process stack,
/// the admin console on the Aspire AppHost). They drive the identity server's own Razor login page and
/// the RP's post-callback landing.
/// </summary>
internal static class FrontEndFlows
{
    public static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);

    /// <summary>Fills and submits the identity server's Razor password form, retrying via a scripted submit.</summary>
    public static async Task FillPasswordAsync(IPage page, string email, string password)
    {
        var emailBox = page.Locator("[data-testid=login-form] input[name='Input.Email']");
        await emailBox.WaitForAsync(new() { Timeout = 20_000 });
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var passwordBox = page.Locator("[data-testid=login-form] input[name='Input.Password']");
        await emailBox.FillAsync(email);
        await passwordBox.FillAsync(password);
        await Expect(emailBox).ToHaveValueAsync(email);

        var loginUrl = page.Url;
        var consoleErrors = new List<string>();
        page.Console += (_, msg) => { if (msg.Type is "error" or "warning") consoleErrors.Add($"{msg.Type}: {msg.Text}"); };
        page.PageError += (_, err) => consoleErrors.Add($"pageerror: {err}");

        await page.ClickAsync("[data-testid=login-submit]");
        try
        {
            await page.WaitForURLAsync(u => u != loginUrl, new() { Timeout = 8_000 });
        }
        catch (TimeoutException)
        {
            // Fall back to a scripted submit and see whether that navigates.
            await page.EvalOnSelectorAsync("[data-testid=login-form]", "f => f.requestSubmit()");
            try
            {
                await page.WaitForURLAsync(u => u != loginUrl, new() { Timeout = 12_000 });
            }
            catch (TimeoutException ex)
            {
                throw new InvalidOperationException(
                    $"Submit did not navigate even via requestSubmit(). url={page.Url}\nconsole:\n{string.Join("\n", consoleErrors)}", ex);
            }
        }
    }

    /// <summary>Clicks sign-out and asserts the RP end-session round-trips back to the app itself.</summary>
    public static async Task SignOutAsync(IPage page, string appUrl)
    {
        await page.ClickAsync("[data-testid=sign-out]");

        // End-session must round-trip back to the app itself, not stall in a redirect loop on the
        // identity server's tenant root (the bug this guards against).
        try
        {
            await page.WaitForURLAsync(
                u => u.StartsWith(appUrl, StringComparison.Ordinal)
                     && !u.Contains("/connect/", StringComparison.Ordinal)
                     && !u.Contains("/auth/oidc/", StringComparison.Ordinal),
                new() { Timeout = 20_000 });
        }
        catch (TimeoutException ex)
        {
            throw new InvalidOperationException(
                $"Sign-out never returned to {appUrl}; stuck at {page.Url} (possible redirect loop).", ex);
        }

        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Expect(page.Locator("[data-testid=sign-in], [data-testid=landing-sign-in]").First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(page.Locator("[data-testid=user-name]")).Not.ToBeVisibleAsync();
    }

    /// <summary>Waits past the OIDC callback hop to a real app page under <paramref name="prefix"/>.</summary>
    public static async Task WaitForAppAsync(IPage page, string prefix)
    {
        try
        {
            // A tenant with passkeys enabled interposes a one-time "set up a passkey" page on the first
            // interactive sign-in. These specs cover the OIDC round-trip, not enrollment — skip past it.
            await page.WaitForURLAsync(
                u => IsAppUrl(u, prefix) || u.Contains(PasskeyEnrollMarker, StringComparison.Ordinal),
                new() { Timeout = 30_000 });

            if (page.Url.Contains(PasskeyEnrollMarker, StringComparison.Ordinal))
            {
                await page.ClickAsync("[data-testid=passkey-enroll-skip]");
                await page.WaitForURLAsync(u => IsAppUrl(u, prefix), new() { Timeout = 30_000 });
            }

            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        }
        catch (TimeoutException ex)
        {
            var body = await page.ContentAsync();
            throw new InvalidOperationException(
                $"Never returned to {prefix}. Stuck at {page.Url}\n--- page ---\n{(body.Length > 4000 ? body[..4000] : body)}", ex);
        }
    }

    private const string PasskeyEnrollMarker = "/identity/account/passkeyenroll";

    private static bool IsAppUrl(string url, string prefix) =>
        url.StartsWith(prefix, StringComparison.Ordinal) && !url.Contains("/auth/oidc/", StringComparison.Ordinal);
}
