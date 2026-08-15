using Microsoft.Playwright;

namespace Huia.Tests.E2E;

/// <summary>
/// Verifies the real "Sign in with Google" wiring on Huia's login page — <c>Huia.TodoApi/Program.cs</c>
/// registers a real Google OAuth client via <c>huia.ExternalLogins.AddGoogle(...)</c> from
/// <c>Google:ClientId</c>/<c>Google:ClientSecret</c> in <c>appsettings.json</c> (or user-secrets/environment
/// variables overriding it — never commit a real client secret to source control) whenever a client id is
/// configured.
/// </summary>
/// <remarks>
/// This deliberately stops at the redirect to Google's own <c>accounts.google.com</c> authorization
/// endpoint. Confirmed against a real run: Google evaluates <c>client_id</c> before <c>redirect_uri</c>, so
/// an <c>invalid_client</c>/"unknown OAuth client" response means the configured id/secret themselves are
/// wrong — that's what this asserts against. A <c>redirect_uri_mismatch</c> response, by contrast, is
/// Google actively telling us the client id *was* recognized, just that this particular
/// Aspire-test-assigned, randomly-ported <c>todoapi</c> URL isn't in that client's Authorized Redirect URIs
/// list in the Google Cloud Console project — expected and out of this test's control in an ephemeral E2E
/// environment, not a wiring bug, so it's treated as a pass here rather than asserting a real sign-in form
/// always renders. Scripting an actual Google sign-in (entering real credentials, clicking through consent)
/// is out of scope regardless — Google's login flow expects a real user and actively challenges automated
/// clients, so there's nothing reliable or appropriate to automate past this boundary. The full challenge →
/// callback → account-linking round trip Huia's own code drives after the provider redirects back is
/// covered end-to-end two other ways instead: against a fake in-process IdP, see
/// <c>tests/Huia.Tests.Integration/ExternalLogin</c>; and, in a real browser against a real second HTTP
/// server, see <see cref="ExternalIdentityServerLoginE2ETests"/>, which scripts the whole thing against
/// <c>samples/Huia.IdentityServer</c> — a controllable Huia-powered external IdP that (unlike Google) can
/// actually be signed into by an automated test.
/// </remarks>
[Collection("Huia E2E")]
public sealed class ExternalLoginE2ETests(HuiaAppFixture fixture) : IAsyncLifetime
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
    public async Task LoginPage_GoogleButton_ChallengesTheRealGoogleClient()
    {
        var apiBaseUrl = GetBaseUrl("todoapi").TrimEnd('/');

        await _page.GotoAsync($"{apiBaseUrl}/identity/account/login");

        var googleButton = _page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Google" });
        await Assertions.Expect(googleButton).ToBeVisibleAsync(new() { Timeout = 30000 });

        await Task.WhenAll(
            _page.WaitForURLAsync(url => string.Equals(new Uri(url).Host, "accounts.google.com", StringComparison.Ordinal),
                new PageWaitForURLOptions { Timeout = 30000 }),
            googleButton.ClickAsync());

        // Google's own server received and evaluated the request against the real, configured client id —
        // see this test's own remarks for why redirect_uri_mismatch is an accepted outcome here, but
        // invalid_client (the id/secret themselves being wrong) is not.
        var body = await _page.Locator("body").InnerTextAsync();
        Assert.DoesNotContain("invalid_client", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("The OAuth client was not found", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("deleted_client", body, StringComparison.OrdinalIgnoreCase);
    }

    private string GetBaseUrl(string resourceName)
    {
        using var client = fixture.App.CreateHttpClient(resourceName);
        return client.BaseAddress!.ToString();
    }
}
