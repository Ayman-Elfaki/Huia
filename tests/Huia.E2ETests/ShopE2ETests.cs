using System.Net.Http;
using System.Text.Json;
using Microsoft.Playwright;
using static Huia.E2ETests.FrontEndFlows;

namespace Huia.E2ETests;

/// <summary>
/// Drives the <c>Shop.App</c>/<c>Shop.Api</c> sample end to end: the first real consumer of
/// <c>nuxt-huia-headless</c> and <c>Huia.Headless</c> together. Covers anonymous browsing, the
/// register/login JSON flow (no hosted account UI — the app owns its own form), the protected cart page's
/// local (non-OIDC) redirect-to-login, and a full add-to-cart/checkout round trip against the real API.
/// </summary>
[Trait("Category", "E2E")]
[Collection("shop")]
public sealed class ShopE2ETests(ShopStackFixture fx)
{
    [SkippableFact]
    public async Task Anonymous_visitors_can_browse_products_but_are_prompted_to_sign_in_to_buy()
    {
        Skip.IfNot(fx.Started, fx.SkipReason ?? "shop stack not started");
        await using var session = await BrowserSession.StartAsync();
        var page = session.Page;

        await page.GotoAsync($"{fx.ShopAppUrl}/");
        await Expect(page.Locator("body")).ToContainTextAsync("Huia Mug", new() { Timeout = 15_000 });

        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Sign in to buy" }).First).ToBeVisibleAsync();
        (await page.GetByRole(AriaRole.Button, new() { Name = "Add to cart" }).CountAsync()).ShouldBe(0);

        // The cart is a locally-protected route (Huia.Headless has no hosted login page to bounce to) —
        // an anonymous visit lands on the app's own /login with a returnTo back to /cart.
        await page.GotoAsync($"{fx.ShopAppUrl}/cart");
        await page.WaitForURLAsync(u => u.Contains("/login", StringComparison.Ordinal), new() { Timeout = 15_000 });
        Assert.Contains("returnTo", page.Url, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task Registers_signs_in_buys_something_and_signs_out()
    {
        Skip.IfNot(fx.Started, fx.SkipReason ?? "shop stack not started");
        await using var session = await BrowserSession.StartAsync();
        var page = session.Page;
        var email = $"e2e-shop-{Guid.NewGuid():N}@huia.local";
        const string password = "Password1!";

        await page.GotoAsync($"{fx.ShopAppUrl}/login");
        await RegisterAsync(page, email, password);
        await LoginAsync(page, email, password);

        // Lands back at "/" (the default returnTo) and the header shows the signed-in user.
        await page.WaitForURLAsync(u => u.TrimEnd('/') == fx.ShopAppUrl.TrimEnd('/'), new() { Timeout = 15_000 });
        await Expect(page.Locator("header")).ToContainTextAsync(email, new() { Timeout = 15_000 });

        // Buy a mug.
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Add to cart" }).First).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Add to cart" }).First.ClickAsync();
        await Expect(page.Locator("body")).ToContainTextAsync("Added to cart.", new() { Timeout = 15_000 });

        // Now allowed onto the cart page directly (session already established) with the item in it.
        await page.GotoAsync($"{fx.ShopAppUrl}/cart");
        await Expect(page.Locator("body")).ToContainTextAsync("mug × 1", new() { Timeout = 15_000 });

        await page.GetByRole(AriaRole.Button, new() { Name = "Checkout" }).ClickAsync();
        await Expect(page.Locator("body")).ToContainTextAsync("placed — total $12.00", new() { Timeout = 15_000 });
        await Expect(page.Locator("body")).ToContainTextAsync("Your cart is empty.", new() { Timeout = 15_000 });

        // Sign out clears the session and the cart route is protected again.
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign out" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Sign in" })).ToBeVisibleAsync(new() { Timeout = 15_000 });

        await page.GotoAsync($"{fx.ShopAppUrl}/cart");
        await page.WaitForURLAsync(u => u.Contains("/login", StringComparison.Ordinal), new() { Timeout = 15_000 });
    }

    [SkippableFact]
    public async Task Rejects_sign_in_with_the_wrong_password()
    {
        Skip.IfNot(fx.Started, fx.SkipReason ?? "shop stack not started");
        await using var session = await BrowserSession.StartAsync();
        var page = session.Page;
        var email = $"e2e-shop-{Guid.NewGuid():N}@huia.local";
        const string password = "Password1!";

        await page.GotoAsync($"{fx.ShopAppUrl}/login");
        await RegisterAsync(page, email, password);

        await page.GetByPlaceholder("Email").FillAsync(email);
        await page.GetByPlaceholder("Password").FillAsync("TotallyWrong1!");
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();

        await Expect(page.Locator("body")).ToContainTextAsync("Sign-in failed.", new() { Timeout = 15_000 });
        Assert.Contains("/login", page.Url, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task Signs_in_with_a_phone_one_time_code_as_a_new_number()
    {
        Skip.IfNot(fx.Started, fx.SkipReason ?? "shop stack not started");
        await using var session = await BrowserSession.StartAsync();
        var page = session.Page;
        var phone = "+1 202 555 0" + Random.Shared.Next(100, 999);

        await page.GotoAsync($"{fx.ShopAppUrl}/login");
        await page.GetByRole(AriaRole.Button, new() { Name = "Phone" }).ClickAsync();
        await page.GetByPlaceholder("+1 202 555 0123").FillAsync(phone);
        await page.GetByRole(AriaRole.Button, new() { Name = "Send code" }).ClickAsync();

        await Expect(page.Locator("body")).ToContainTextAsync("We sent a code", new() { Timeout = 15_000 });
        var code = await ReadOtpAsync(phone);
        await page.GetByPlaceholder("123456").FillAsync(code);
        await page.GetByRole(AriaRole.Button, new() { Name = "Verify" }).ClickAsync();

        // A brand-new number has no name on file yet.
        await Expect(page.Locator("body")).ToContainTextAsync("what's your name", new() { Timeout = 15_000 });
        await page.GetByPlaceholder("First name").FillAsync("Percy");
        await page.GetByPlaceholder("Last name").FillAsync("Phone");
        await page.GetByRole(AriaRole.Button, new() { Name = "Finish" }).ClickAsync();

        await page.WaitForURLAsync(u => u.TrimEnd('/') == fx.ShopAppUrl.TrimEnd('/'), new() { Timeout = 15_000 });
        await Expect(page.Locator("header")).ToContainTextAsync("Percy", new() { Timeout = 15_000 });
    }

    [SkippableFact]
    public async Task Signs_in_with_the_partner_external_provider()
    {
        Skip.IfNot(fx.Started, fx.SkipReason ?? "shop stack not started");
        await using var session = await BrowserSession.StartAsync();
        var page = session.Page;

        await page.GotoAsync($"{fx.ShopAppUrl}/login");
        await page.GetByRole(AriaRole.Link, new() { Name = "Sign in with Partner" }).ClickAsync();

        // Now on Huia.External's own Razor login page — the real upstream IdP, not a stand-in.
        await page.WaitForURLAsync(u => u.Contains("/partners/identity/account/", StringComparison.Ordinal), new() { Timeout = 25_000 });
        await FillPasswordAsync(page, "full@partners.test", "Partner1!Pass");

        // First time this partner identity signs into Shop.Api's own (separate) tenant, so it needs a
        // name before the account is created — same as Huia.OpenId's CompleteProfile step.
        await Expect(page.Locator("body")).ToContainTextAsync("what's your name", new() { Timeout = 25_000 });
        await page.GetByRole(AriaRole.Button, new() { Name = "Finish" }).ClickAsync();

        await page.WaitForURLAsync(u => u.TrimEnd('/') == fx.ShopAppUrl.TrimEnd('/'), new() { Timeout = 15_000 });
        await Expect(page.Locator("header")).ToContainTextAsync("full@partners.test", new() { Timeout = 15_000 });
    }

    [SkippableFact]
    public async Task Rejects_a_wrong_phone_code()
    {
        Skip.IfNot(fx.Started, fx.SkipReason ?? "shop stack not started");
        await using var session = await BrowserSession.StartAsync();
        var page = session.Page;
        var phone = "+1 202 555 0" + Random.Shared.Next(100, 999);

        await page.GotoAsync($"{fx.ShopAppUrl}/login");
        await page.GetByRole(AriaRole.Button, new() { Name = "Phone" }).ClickAsync();
        await page.GetByPlaceholder("+1 202 555 0123").FillAsync(phone);
        await page.GetByRole(AriaRole.Button, new() { Name = "Send code" }).ClickAsync();
        await Expect(page.Locator("body")).ToContainTextAsync("We sent a code", new() { Timeout = 15_000 });

        var realCode = await ReadOtpAsync(phone);
        var wrongCode = realCode[0] == '0' ? "199999" : "000000";
        await page.GetByPlaceholder("123456").FillAsync(wrongCode);
        await page.GetByRole(AriaRole.Button, new() { Name = "Verify" }).ClickAsync();

        await Expect(page.Locator("body")).ToContainTextAsync("did not work", new() { Timeout = 15_000 });
    }

    private async Task<string> ReadOtpAsync(string e164)
    {
        using var http = new HttpClient();
        var normalized = e164.Replace(" ", "");
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var response = await http.GetAsync($"{fx.ShopApiUrl}/e2e-otp?phone={Uri.EscapeDataString(normalized)}");
            if (response.IsSuccessStatusCode)
            {
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                return json.RootElement.GetProperty("code").GetString()!;
            }

            await Task.Delay(500);
        }

        throw new InvalidOperationException($"No OTP was captured for {e164}.");
    }

    private static async Task RegisterAsync(IPage page, string email, string password)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Don't have an account? Register" }).ClickAsync();
        await page.GetByPlaceholder("Email").FillAsync(email);
        await page.GetByPlaceholder("Password").FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Create account", Exact = true }).ClickAsync();
        await Expect(page.Locator("body")).ToContainTextAsync("Account created", new() { Timeout = 15_000 });
    }

    private static async Task LoginAsync(IPage page, string email, string password)
    {
        // register() flips the form back to login mode but leaves the fields as-is; fill fresh anyway.
        await page.GetByPlaceholder("Email").FillAsync(email);
        await page.GetByPlaceholder("Password").FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
    }
}
