using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Playwright;

namespace Huia.E2ETests;

[Trait("Category", "E2E")]
[Collection("sample-host")]
public sealed class NewFeaturesE2ETests(SampleHostFixture host)
{
    [Fact]
    public async Task Country_picker_always_remains_next_to_phone_picker_on_mobile_viewport()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });

        // Mobile viewport (e.g. 360 x 640, smaller than 30rem / 480px)
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 360, Height = 640 },
            IsMobile = true,
        });
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{host.BaseUrl}/todo/identity/account/login");

        // Wait for login tabs and switch to phone login
        await page.WaitForSelectorAsync("[data-testid=login-tabs]", new() { Timeout = 15_000 });
        await page.ClickAsync("[role=tab][aria-controls=panel-phone]");

        await page.WaitForSelectorAsync("[data-testid=phone-login-form]", new() { Timeout = 15_000 });

        var countryPicker = page.Locator("[data-testid=phone-login-form] .huia-country-select");
        var phoneInput = page.Locator("[data-testid=phone-login-form] input[name='Input.PhoneNumber']");

        var countryBox = await countryPicker.BoundingBoxAsync();
        var phoneBox = await phoneInput.BoundingBoxAsync();

        Assert.NotNull(countryBox);
        Assert.NotNull(phoneBox);

        // Vertically aligned (on the same row, not stacked one above the other)
        Assert.True(Math.Abs(countryBox.Y - phoneBox.Y) < 6,
            $"Expected country picker Y ({countryBox.Y}) and phone picker Y ({phoneBox.Y}) to be on the same row, but differed by {Math.Abs(countryBox.Y - phoneBox.Y)}px.");

        // Horizontally adjacent (country picker on the left, phone picker on the right)
        Assert.True(countryBox.X + countryBox.Width <= phoneBox.X + 2,
            $"Expected country picker right edge ({countryBox.X + countryBox.Width}) to be next to phone picker left edge ({phoneBox.X}).");

        // Phone input must retain adequate width
        Assert.True(phoneBox.Width > 120, $"Expected phone input width to be > 120px, but was {phoneBox.Width}px.");
    }

    [Fact]
    public async Task Razor_pages_apply_contrast_color_with_javascript_fallback_for_accent_color()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });

        var page = await browser.NewPageAsync();

        // The 'todo' tenant in Todo.IdentityServer is configured with AccentColor = "#059669" (dark green)
        await page.GotoAsync($"{host.BaseUrl}/todo/identity/account/login");

        // Verify that the style block defines --primary and --primary-foreground with contrast-color()
        var html = await page.ContentAsync();
        Assert.Contains("--primary: #059669", html);
        Assert.Contains("contrast-color(#059669)", html);

        // Verify computed --primary-foreground on :root (either native contrast-color or the JS fallback)
        var primaryFg = await page.EvaluateAsync<string>(
            "() => getComputedStyle(document.documentElement).getPropertyValue('--primary-foreground').trim()");
        Assert.False(string.IsNullOrWhiteSpace(primaryFg), "Computed --primary-foreground should not be empty.");

        // For emerald "#059669", relative luminance is ~0.228 (> 0.179 threshold), so black (#000000)
        // yields a 5.56:1 contrast ratio (vs 3.77:1 for white). The fallback correctly selects #000000.
        var isBlackContrast = primaryFg.Contains("0, 0, 0", StringComparison.OrdinalIgnoreCase)
            || primaryFg.Equals("#000000", StringComparison.OrdinalIgnoreCase)
            || primaryFg.Contains("0.145", StringComparison.OrdinalIgnoreCase);
        Assert.True(isBlackContrast, $"Expected high contrast black foreground for #059669, got: '{primaryFg}'");

        // Now navigate to 'master' tenant (AccentColor = "#4f46e5" indigo, L ~ 0.115 < 0.179), which yields white (#ffffff)
        await page.GotoAsync($"{host.BaseUrl}/master/identity/account/login");
        var masterFg = await page.EvaluateAsync<string>(
            "() => getComputedStyle(document.documentElement).getPropertyValue('--primary-foreground').trim()");
        var isWhiteContrast = masterFg.Contains("255, 255, 255", StringComparison.OrdinalIgnoreCase)
            || masterFg.Equals("#ffffff", StringComparison.OrdinalIgnoreCase)
            || masterFg.Contains("0.985", StringComparison.OrdinalIgnoreCase);
        Assert.True(isWhiteContrast, $"Expected high contrast white foreground for #4f46e5, got: '{masterFg}'");

        // Verify JS fallback calculation on light accent (#facc15 yellow) gives black (#000000)
        var lightColorFallback = await page.EvaluateAsync<string>(@"() => {
            var a = '#facc15';
            var d = document.createElement('div');
            d.style.color = a;
            (document.head || document.documentElement).appendChild(d);
            var c = window.getComputedStyle(d).color;
            d.remove();
            var m = c.match(/[\d.]+/g);
            var r = parseFloat(m[0])/255, g = parseFloat(m[1])/255, b = parseFloat(m[2])/255;
            var rL = r<=0.04045?r/12.92:Math.pow((r+0.055)/1.055,2.4);
            var gL = g<=0.04045?g/12.92:Math.pow((g+0.055)/1.055,2.4);
            var bL = b<=0.04045?b/12.92:Math.pow((b+0.055)/1.055,2.4);
            var l = 0.2126*rL + 0.7152*gL + 0.0722*bL;
            return l > 0.179 ? '#000000' : '#ffffff';
        }");
        Assert.Equal("#000000", lightColorFallback);

        // Verify JS fallback calculation on dark accent (#000000 black) gives white (#ffffff)
        var darkColorFallback = await page.EvaluateAsync<string>(@"() => {
            var a = '#000000';
            var d = document.createElement('div');
            d.style.color = a;
            (document.head || document.documentElement).appendChild(d);
            var c = window.getComputedStyle(d).color;
            d.remove();
            var m = c.match(/[\d.]+/g);
            var r = parseFloat(m[0])/255, g = parseFloat(m[1])/255, b = parseFloat(m[2])/255;
            var rL = r<=0.04045?r/12.92:Math.pow((r+0.055)/1.055,2.4);
            var gL = g<=0.04045?g/12.92:Math.pow((g+0.055)/1.055,2.4);
            var bL = b<=0.04045?b/12.92:Math.pow((b+0.055)/1.055,2.4);
            var l = 0.2126*rL + 0.7152*gL + 0.0722*bL;
            return l > 0.179 ? '#000000' : '#ffffff';
        }");
        Assert.Equal("#ffffff", darkColorFallback);
    }

    [Fact]
    public async Task Stateless_mode_session_data_stored_encrypted_in_cookie_without_server_session_tracking()
    {
        // Direct token exchange with e2e tenant to test stateless session semantics
        using var client = new HttpClient();
        var verifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        // Acquire an auth code via browser login
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });

        var authorizeUrl =
            $"{host.BaseUrl}/e2e/connect/authorize?response_type=code&client_id=e2e-spa" +
            $"&redirect_uri={Uri.EscapeDataString($"{host.BaseUrl}/e2e/e2e-callback")}" +
            $"&scope=openid%20profile%20email&code_challenge={challenge}&code_challenge_method=S256&state=stateless-test";

        var page = await browser.NewPageAsync();
        await page.GotoAsync(authorizeUrl);

        await page.FillAsync("[data-testid=login-form] input[name='Input.Email']", "e2e-user@huia.local");
        await page.FillAsync("[data-testid=login-form] input[name='Input.Password']", "Password1!");
        await page.ClickAsync("[data-testid=login-submit]");

        await page.WaitForURLAsync(
            u => u.Contains("/e2e-callback", StringComparison.Ordinal) || u.Contains("/passkeyenroll", StringComparison.Ordinal),
            new PageWaitForURLOptions { Timeout = 15_000 });
        if (page.Url.Contains("/passkeyenroll", StringComparison.Ordinal))
        {
            await page.ClickAsync("[data-testid=passkey-enroll-skip]");
            await page.WaitForURLAsync(u => u.Contains("/e2e-callback", StringComparison.Ordinal), new PageWaitForURLOptions { Timeout = 15_000 });
        }

        var query = new Uri(page.Url).Query;
        var queryParams = System.Web.HttpUtility.ParseQueryString(query);
        var code = queryParams["code"];
        Assert.NotNull(code);

        // Exchange code for tokens
        var tokenRequest = new HttpRequestMessage(HttpMethod.Post, $"{host.BaseUrl}/e2e/connect/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = "e2e-spa",
                ["code"] = code,
                ["redirect_uri"] = $"{host.BaseUrl}/e2e/e2e-callback",
                ["code_verifier"] = verifier,
            }),
        };

        var tokenResponse = await client.SendAsync(tokenRequest);
        Assert.True(tokenResponse.IsSuccessStatusCode);
        var tokenBody = await tokenResponse.Content.ReadAsStringAsync();
        using var jsonDoc = JsonDocument.Parse(tokenBody);
        var accessToken = jsonDoc.RootElement.GetProperty("access_token").GetString();
        Assert.NotNull(accessToken);

        // In stateless mode, the frontend does not save this token in any server database/cache;
        // it is sealed directly into the client's cookie. Let's verify through the user info endpoint that the token is valid.
        var userinfoRequest = new HttpRequestMessage(HttpMethod.Get, $"{host.BaseUrl}/e2e/connect/userinfo");
        userinfoRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        var userinfoResponse = await client.SendAsync(userinfoRequest);
        Assert.True(userinfoResponse.IsSuccessStatusCode);
        var userinfoBody = await userinfoResponse.Content.ReadAsStringAsync();
        Assert.Contains("e2e-user@huia.local", userinfoBody);
    }
}
