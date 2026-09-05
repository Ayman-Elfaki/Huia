using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Playwright;

namespace Huia.E2ETests;

[Trait("Category", "E2E")]
[Collection("sample-host")]
public sealed class InteractiveSignInTests(SampleHostFixture host)
{
    [SkippableFact]
    public async Task A_user_signs_in_through_the_browser_and_the_client_gets_a_code()
    {
        Skip.IfNot(host.Started, "The sample host did not start; skipping the browser test.");

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });

        var verifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var authorizeUrl =
            $"{host.BaseUrl}/e2e/connect/authorize?response_type=code&client_id=e2e-spa" +
            $"&redirect_uri={Uri.EscapeDataString($"{host.BaseUrl}/e2e/e2e-callback")}" +
            $"&scope=openid%20profile%20email&code_challenge={challenge}&code_challenge_method=S256&state=xyz";

        var page = await browser.NewPageAsync();
        await page.GotoAsync(authorizeUrl);

        await page.FillAsync("[data-testid=login-form] input[name='Input.Email']", "e2e-user@huia.local");
        await page.FillAsync("[data-testid=login-form] input[name='Input.Password']", "Password1!");
        await page.ClickAsync("[data-testid=login-submit]");

        // The e2e tenant has passkeys enabled, so a first sign-in shows the one-time "set up a passkey"
        // page before the client callback. This spec is about the code hand-off — skip enrollment.
        await page.WaitForURLAsync(
            u => u.Contains("/e2e-callback", StringComparison.Ordinal) || u.Contains("/passkeyenroll", StringComparison.Ordinal),
            new PageWaitForURLOptions { Timeout = 15_000 });
        if (page.Url.Contains("/passkeyenroll", StringComparison.Ordinal))
        {
            await page.ClickAsync("[data-testid=passkey-enroll-skip]");
        }

        await page.WaitForURLAsync("**/e2e-callback**", new PageWaitForURLOptions { Timeout = 15_000 });

        // The authorization code is delivered on the redirect back to the client's callback.
        page.Url.ShouldContain("code=");
        page.Url.ShouldContain("state=xyz");
    }
}
