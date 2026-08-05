using System.Text.Json;
using Microsoft.Playwright;

namespace Huia.Tests.E2E;

/// <summary>
/// Drives the real Huia Todo sample end to end through a browser: register a new account through Huia's
/// own Identity UI, complete the OIDC redirect back to the Next.js app, then create/toggle/delete a todo
/// through the real UI.
/// </summary>
[Collection("Huia E2E")]
public sealed class TodoAppE2ETests(HuiaAppFixture fixture) : IAsyncLifetime
{
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;
    private IPage _page = null!;

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync();

        // TodoApi runs on Aspire's local dev-cert HTTPS endpoint, which Node/.NET trust automatically (via
        // injected CA bundles) but a real browser does not.
        _page = await _browser.NewPageAsync(new BrowserNewPageOptions { IgnoreHTTPSErrors = true });
    }

    public async Task DisposeAsync()
    {
        await _browser.DisposeAsync();
        _playwright.Dispose();
    }

    [Fact]
    public async Task RegisterSignInCreateToggleDeleteTodo_Succeeds()
    {
        var webBaseUrl = GetBaseUrl("web");
        var email = $"e2e-{Guid.NewGuid():N}@example.com";
        const string password = "P@ssw0rd123!";

        await RegisterAndSignInAsync(webBaseUrl, "E2E", "Tester", email, password);

        // The header shows the signed-in user's given/family name (from the OIDC profile claims), not
        // their raw email — see page.tsx.
        await Assertions.Expect(_page.GetByText("E2E Tester")).ToBeVisibleAsync();

        await _page.GetByPlaceholder("What needs doing?").FillAsync("Buy milk");
        await _page.GetByRole(AriaRole.Button, new() { Name = "Add" }).ClickAsync();
        // The title renders inside an editable <input> (inline rename), so it's a value, not text content.
        await Assertions.Expect(_page.Locator("input[value='Buy milk']")).ToBeVisibleAsync();

        await _page.GetByLabel("Mark as done").ClickAsync();
        await Assertions.Expect(_page.GetByLabel("Mark as not done")).ToBeVisibleAsync();

        await _page.GetByRole(AriaRole.Button, new() { Name = "Delete" }).ClickAsync();
        await Assertions.Expect(_page.GetByText("No todos yet")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Registering_SendsConfirmationEmail_ViaMailpit()
    {
        var webBaseUrl = GetBaseUrl("web");
        using var mailpitClient = fixture.App.CreateHttpClient("mailpit", "http");
        var email = $"e2e-mail-{Guid.NewGuid():N}@example.com";

        await RegisterAndSignInAsync(webBaseUrl, "E2E", "Mail", email, "P@ssw0rd123!");

        using var response = await mailpitClient.GetAsync($"/api/v1/search?query=to:{email}");
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("messages_count").GetInt32() >= 1);
    }

    private async Task RegisterAndSignInAsync(string webBaseUrl, string firstName, string lastName, string email,
        string password)
    {
        await _page.GotoAsync(webBaseUrl);
        await _page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();

        // Lands on Huia's own login page; follow the link to registration instead.
        await _page.GetByRole(AriaRole.Link, new() { Name = "Create an account" }).ClickAsync();

        await _page.GetByLabel("First name").FillAsync(firstName);
        await _page.GetByLabel("Last name").FillAsync(lastName);
        await _page.GetByLabel("Email").FillAsync(email);
        await _page.GetByLabel("Password", new() { Exact = true }).FillAsync(password);
        await _page.GetByLabel("Confirm password").FillAsync(password);
        await _page.GetByRole(AriaRole.Button, new() { Name = "Create account" }).ClickAsync();

        // RequireConfirmedAccount is false, so registration signs the user in and the OIDC round trip lands
        // back on the Next.js app, authenticated.
        await _page.WaitForURLAsync($"{webBaseUrl}*", new() { Timeout = 60000 });
    }

    private string GetBaseUrl(string resourceName)
    {
        using var client = fixture.App.CreateHttpClient(resourceName);
        return client.BaseAddress!.ToString();
    }
}