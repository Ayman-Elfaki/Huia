using System.Net.Http;
using System.Text.Json;
using Microsoft.Playwright;
using static Huia.E2ETests.FrontEndFlows;

namespace Huia.E2ETests;

/// <summary>
/// Browser-drives the Todo.App Nuxt front-end through real OIDC sign-in / sign-out (password, phone and
/// external provider). The admin console has its own Aspire-hosted spec (<see cref="AdminUiE2ETests"/>).
/// </summary>
[Trait("Category", "E2E")]
[Collection("frontend-stack")]
public sealed class FrontEndAuthE2ETests(FrontEndStackFixture stack)
{
    [SkippableFact]
    public async Task Todo_App_signs_in_with_password_and_signs_out()
    {
        Skip.IfNot(stack.Started, stack.SkipReason ?? "stack not started");
        await using var session = await BrowserSession.StartAsync();
        var page = session.Page;

        await page.GotoAsync($"{stack.TodoAppUrl}/");
        await page.ClickAsync("[data-testid=landing-sign-in]");

        await FillPasswordAsync(page, "alice@todo.test", "Password1!2345");

        await WaitForAppAsync(page, stack.TodoAppUrl);
        await Expect(page.Locator("[data-testid=user-name]")).ToBeVisibleAsync();

        await SignOutAsync(page, stack.TodoAppUrl);
    }

    [SkippableFact]
    public async Task Todo_App_signs_in_with_a_phone_one_time_code()
    {
        Skip.IfNot(stack.Started, stack.SkipReason ?? "stack not started");
        await using var session = await BrowserSession.StartAsync();
        var page = session.Page;

        await page.GotoAsync($"{stack.TodoAppUrl}/");
        await page.ClickAsync("[data-testid=landing-sign-in]");

        await page.WaitForSelectorAsync("[data-testid=login-tabs]", new() { Timeout = 15_000 });
        await page.ClickAsync("[role=tab][aria-controls=panel-phone]");

        var phone = "+1 202 555 0143";
        // The country picker is a Basecoat listbox (search + click), not a native <select>.
        await page.ClickAsync("[data-testid=phone-login-form] .huia-country-select > button");
        await page.FillAsync("[data-testid=phone-login-form] .huia-country-select header input", "United States");
        await page.ClickAsync("[data-testid=phone-login-form] .huia-country-select [role=option][data-value='US']");
        await page.FillAsync("[data-testid=phone-login-form] input[name='Input.PhoneNumber']", phone);
        await page.ClickAsync("[data-testid=phone-login-submit]");

        await page.WaitForSelectorAsync("[data-testid=verify-otp-form]", new() { Timeout = 15_000 });
        var code = await ReadOtpAsync("+12025550143");
        await FillOtpAsync(page, code);
        await page.ClickAsync("[data-testid=verify-otp-submit]");

        // A brand-new phone-only account is routed through CompleteProfile.
        if (await page.Locator("[data-testid=complete-profile-form]").IsVisibleAsync())
        {
            await page.FillAsync("input[name='Input.FirstName']", "Percy");
            await page.FillAsync("input[name='Input.LastName']", "Phone");
            await page.ClickAsync("[data-testid=complete-profile-submit]");
        }

        await WaitForAppAsync(page, stack.TodoAppUrl);
        await Expect(page.Locator("[data-testid=user-name]")).ToBeVisibleAsync();
    }

    [SkippableFact]
    public async Task Todo_App_rejects_a_wrong_one_time_code_and_stays_on_the_verify_page()
    {
        Skip.IfNot(stack.Started, stack.SkipReason ?? "stack not started");
        await using var session = await BrowserSession.StartAsync();
        var page = session.Page;

        await page.GotoAsync($"{stack.TodoAppUrl}/");
        await page.ClickAsync("[data-testid=landing-sign-in]");

        await page.WaitForSelectorAsync("[data-testid=login-tabs]", new() { Timeout = 15_000 });
        await page.ClickAsync("[role=tab][aria-controls=panel-phone]");

        var phone = "+1 202 555 0177";
        await page.ClickAsync("[data-testid=phone-login-form] .huia-country-select > button");
        await page.FillAsync("[data-testid=phone-login-form] .huia-country-select header input", "United States");
        await page.ClickAsync("[data-testid=phone-login-form] .huia-country-select [role=option][data-value='US']");
        await page.FillAsync("[data-testid=phone-login-form] input[name='Input.PhoneNumber']", phone);
        await page.ClickAsync("[data-testid=phone-login-submit]");

        await page.WaitForSelectorAsync("[data-testid=verify-otp-form]", new() { Timeout = 15_000 });

        // Read the real code, then submit a different one.
        var realCode = await ReadOtpAsync("+12025550177");
        var wrongCode = realCode[0] == '0' ? "199999" : "000000";
        await FillOtpAsync(page, wrongCode);
        await page.ClickAsync("[data-testid=verify-otp-submit]");

        await Expect(page.Locator("[data-testid=otp-error]")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(page.Locator("[data-testid=verify-otp-form]")).ToBeVisibleAsync();
        Assert.Contains("/identity/account/verifyotp", page.Url, StringComparison.Ordinal);

        // The same flow still accepts the correct code afterwards.
        await FillOtpAsync(page, realCode);
        await page.ClickAsync("[data-testid=verify-otp-submit]");
        if (await page.Locator("[data-testid=complete-profile-form]").IsVisibleAsync())
        {
            await page.FillAsync("input[name='Input.FirstName']", "Wanda");
            await page.FillAsync("input[name='Input.LastName']", "Wrong");
            await page.ClickAsync("[data-testid=complete-profile-submit]");
        }

        await WaitForAppAsync(page, stack.TodoAppUrl);
        await Expect(page.Locator("[data-testid=user-name]")).ToBeVisibleAsync();
    }

    [SkippableFact]
    public async Task Todo_App_forwards_the_selected_locale_to_the_Huia_sign_in_page()
    {
        Skip.IfNot(stack.Started, stack.SkipReason ?? "stack not started");
        await using var session = await BrowserSession.StartAsync();
        var page = session.Page;

        await page.GotoAsync($"{stack.TodoAppUrl}/");

        // Switch the app to Arabic; the app itself flips to RTL.
        await page.ClickAsync("[data-testid=locale-switcher]");
        await page.ClickAsync("[data-testid=locale-option-ar]");
        await Expect(page.Locator("html")).ToHaveAttributeAsync("dir", "rtl", new() { Timeout = 10_000 });

        await page.ClickAsync("[data-testid=landing-sign-in]");

        // The ui_locales=ar hint must reach /connect/authorize and localize the account UI too.
        await page.WaitForURLAsync(
            u => u.Contains("/identity/account/login", StringComparison.Ordinal),
            new() { Timeout = 20_000 });
        await Expect(page.Locator("html")).ToHaveAttributeAsync("dir", "rtl", new() { Timeout = 10_000 });
    }

    [SkippableFact]
    public async Task Todo_App_signs_in_through_the_external_provider()
    {
        Skip.IfNot(stack.Started, stack.SkipReason ?? "stack not started");
        await using var session = await BrowserSession.StartAsync();
        var page = session.Page;

        await page.GotoAsync($"{stack.TodoAppUrl}/");
        await page.ClickAsync("[data-testid=landing-sign-in]");

        await page.WaitForSelectorAsync("[data-testid=external-providers]", new() { Timeout = 15_000 });
        await page.ClickAsync("[data-testid=external-providers] button");

        // Now on Huia.External's own Razor login page.
        await page.WaitForURLAsync(u => u.Contains("/partners/identity/account/", StringComparison.Ordinal), new() { Timeout = 25_000 });
        await FillPasswordAsync(page, "full@partners.test", "Partner1!Pass");

        if (await page.Locator("[data-testid=complete-profile-form]").IsVisibleAsync())
        {
            await page.ClickAsync("[data-testid=complete-profile-submit]");
        }

        await WaitForAppAsync(page, stack.TodoAppUrl);
        await Expect(page.Locator("[data-testid=user-name]")).ToBeVisibleAsync();
    }

    [SkippableFact]
    public async Task External_sign_in_links_to_an_existing_account_with_the_same_email()
    {
        Skip.IfNot(stack.Started, stack.SkipReason ?? "stack not started");
        await using var session = await BrowserSession.StartAsync();
        var page = session.Page;

        await page.GotoAsync($"{stack.TodoAppUrl}/");
        await page.ClickAsync("[data-testid=landing-sign-in]");

        await page.WaitForSelectorAsync("[data-testid=external-providers]", new() { Timeout = 15_000 });
        await page.ClickAsync("[data-testid=external-providers] button");

        await page.WaitForURLAsync(u => u.Contains("/partners/identity/account/", StringComparison.Ordinal), new() { Timeout = 25_000 });
        // link@partners.test also exists downstream (password, confirmed email), and the todo tenant
        // has LinkExistingAccountsByEmail — so this links, no profile step.
        await FillPasswordAsync(page, "link@partners.test", "Partner1!Pass");

        await WaitForAppAsync(page, stack.TodoAppUrl);
        await Expect(page.Locator("[data-testid=user-name]")).ToBeVisibleAsync();
        (await page.Locator("[data-testid=complete-profile-form]").IsVisibleAsync()).ShouldBeFalse();
    }

    [SkippableFact]
    public async Task Signing_out_of_the_Todo_app_ends_the_partner_session()
    {
        Skip.IfNot(stack.Started, stack.SkipReason ?? "stack not started");
        await using var session = await BrowserSession.StartAsync();
        var page = session.Page;

        await page.GotoAsync($"{stack.TodoAppUrl}/");
        await page.ClickAsync("[data-testid=landing-sign-in]");
        await page.WaitForSelectorAsync("[data-testid=external-providers]", new() { Timeout = 15_000 });
        await page.ClickAsync("[data-testid=external-providers] button");
        await page.WaitForURLAsync(u => u.Contains("/partners/identity/account/", StringComparison.Ordinal), new() { Timeout = 25_000 });
        await FillPasswordAsync(page, "full@partners.test", "Partner1!Pass");
        if (await page.Locator("[data-testid=complete-profile-form]").IsVisibleAsync())
        {
            await page.ClickAsync("[data-testid=complete-profile-submit]");
        }

        await WaitForAppAsync(page, stack.TodoAppUrl);
        await SignOutAsync(page, stack.TodoAppUrl);

        // Start the partner sign-in again — the partner must ask for credentials, not silently re-auth.
        await page.ClickAsync("[data-testid=landing-sign-in]");
        await page.WaitForSelectorAsync("[data-testid=external-providers]", new() { Timeout = 15_000 });
        await page.ClickAsync("[data-testid=external-providers] button");

        await page.WaitForURLAsync(
            u => u.Contains("/partners/identity/account/login", StringComparison.Ordinal),
            new() { Timeout = 25_000 });
    }

    private static async Task FillOtpAsync(IPage page, string code)
    {
        var boxes = page.Locator(".huia-otp input");
        var count = await boxes.CountAsync();
        if (count == code.Length)
        {
            for (var i = 0; i < count; i++)
            {
                await boxes.Nth(i).FillAsync(code[i].ToString());
            }
        }
        else
        {
            await page.EvaluateAsync(
                "c => { const el = document.getElementById('Input_Code'); el.value = c; el.dispatchEvent(new Event('input', { bubbles: true })); }",
                code);
        }
    }

    private async Task<string> ReadOtpAsync(string e164)
    {
        using var http = new HttpClient();
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var response = await http.GetAsync($"{stack.Issuer}/e2e-otp?phone={Uri.EscapeDataString(e164)}");
            if (response.IsSuccessStatusCode)
            {
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                return json.RootElement.GetProperty("code").GetString()!;
            }

            await Task.Delay(500);
        }

        throw new InvalidOperationException($"No OTP was captured for {e164}.");
    }
}
