using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Huia.Tests.E2E;

/// <summary>
/// Drives the real ConfirmEmail "Sign in" link end to end through a browser, covering the case a pure
/// integration test (see <c>Huia.Tests.Integration.ConfirmationEmailReturnUrlTests</c>) can't: what happens
/// when the link is actually clicked by a browser that still has Huia's own sign-in session from the
/// registration that triggered the email. Login.cshtml.cs's "already signed in" branch is supposed to redirect
/// straight to that returnUrl rather than showing a stale login form — this proves it lands back on the real
/// Next.js app (via <c>ClientHomeResolver</c>'s HomeUri) instead of Huia's own bare root.
/// </summary>
[Collection("Huia E2E")]
public sealed partial class HomeUriE2ETests(HuiaAppFixture fixture) : IAsyncLifetime
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
    public async Task ConfirmEmail_SignInLink_ClickedWhileStillSignedIn_RedirectsStraightBackToTheApp()
    {
        var webBaseUrl = GetBaseUrl("web");
        var email = $"e2e-homeuri-{Guid.NewGuid():N}@example.com";
        const string password = "P@ssw0rd123!";

        // RequireConfirmedAccount is false in this sample, so this both creates the account and signs the
        // browser in — the confirmation email is a purely out-of-band link, and the session it leaves behind
        // is exactly what this test needs still active when that link is clicked below.
        await RegisterAsync(webBaseUrl, email, password);
        // The header shows the signed-in user's given/family name (from the OIDC profile claims), not
        // their raw email — see page.tsx.
        await Assertions.Expect(_page.GetByText("E2E HomeUri")).ToBeVisibleAsync();

        var confirmationLink = await GetConfirmationLinkFromMailpitAsync(email);

        await _page.GotoAsync(confirmationLink);
        await Assertions.Expect(_page.GetByRole(AriaRole.Link, new() { Name = "Sign in" })).ToBeVisibleAsync();

        await _page.GetByRole(AriaRole.Link, new() { Name = "Sign in" }).ClickAsync();

        // Still signed in to Huia, so Login.cshtml.cs's OnGetAsync redirects immediately instead of rendering
        // its form — landing back on the Next.js app's own origin (the client's HomeUri), not a login page.
        await _page.WaitForURLAsync($"{webBaseUrl}*", new() { Timeout = 30000 });
        // The header shows the signed-in user's given/family name (from the OIDC profile claims), not
        // their raw email — see page.tsx.
        await Assertions.Expect(_page.GetByText("E2E HomeUri")).ToBeVisibleAsync();
    }

    private async Task RegisterAsync(string webBaseUrl, string email, string password)
    {
        await _page.GotoAsync(webBaseUrl);
        await _page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
        await _page.GetByRole(AriaRole.Link, new() { Name = "Create an account" }).ClickAsync();

        await _page.GetByLabel("First name").FillAsync("E2E");
        await _page.GetByLabel("Last name").FillAsync("HomeUri");
        await _page.GetByLabel("Email").FillAsync(email);
        await _page.GetByLabel("Password", new() { Exact = true }).FillAsync(password);
        await _page.GetByLabel("Confirm password").FillAsync(password);
        await _page.GetByRole(AriaRole.Button, new() { Name = "Create account" }).ClickAsync();

        await _page.WaitForURLAsync($"{webBaseUrl}*", new() { Timeout = 60000 });
    }

    /// <summary>
    /// Polls Mailpit API for the confirmation email <see cref="RegisterAsync"/>
    /// triggers and pulls the confirmation link out of its HTML body.
    /// </summary>
    private async Task<string> GetConfirmationLinkFromMailpitAsync(string email)
    {
        using var mailpitClient = fixture.App.CreateHttpClient("mailpit", "http");

        string? messageId = null;
        for (var attempt = 0; attempt < 20 && messageId is null; attempt++)
        {
            using var searchResponse = await mailpitClient.GetAsync($"/api/v1/search?query=to:{email}");
            searchResponse.EnsureSuccessStatusCode();

            using var searchDocument = JsonDocument.Parse(await searchResponse.Content.ReadAsStringAsync());
            var messages = searchDocument.RootElement.GetProperty("messages");
            if (messages.GetArrayLength() > 0)
            {
                messageId = messages[0].GetProperty("ID").GetString();
                break;
            }

            await Task.Delay(500);
        }

        Assert.NotNull(messageId);

        using var messageResponse = await mailpitClient.GetAsync($"/api/v1/message/{messageId}");
        messageResponse.EnsureSuccessStatusCode();

        using var messageDocument = JsonDocument.Parse(await messageResponse.Content.ReadAsStringAsync());
        var html = messageDocument.RootElement.GetProperty("HTML").GetString() ?? "";

        var match = ConfirmationLinkPattern().Match(html);
        Assert.True(match.Success, $"No confirmation link found in the email body: {html}");

        return WebUtility.HtmlDecode(match.Groups["link"].Value);
    }

    private string GetBaseUrl(string resourceName)
    {
        using var client = fixture.App.CreateHttpClient(resourceName);
        return client.BaseAddress!.ToString();
    }

    [GeneratedRegex("href=\"(?<link>[^\"]*ConfirmEmail[^\"]*)\"",
        RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ConfirmationLinkPattern();
}
