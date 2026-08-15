using Microsoft.Playwright;

namespace Huia.Tests.E2E;

/// <summary>
/// Drives the full external-login round trip in a real browser against a real, controllable second HTTP
/// server: <c>samples/Huia.IdentityServer</c>, a second Huia instance registered on TodoApi twice — once as
/// the generic <c>huia-idp</c> <c>AddOpenIdConnect</c> provider requesting the full profile, and once as
/// <c>huia-idp-partial</c>, the same client requesting no "profile" scope at all (see
/// <c>Huia.TodoApi/Program.cs</c> and docs/external-providers.md). Unlike <see cref="ExternalLoginE2ETests"/>,
/// which has to stop at the redirect to the real accounts.google.com (scripting a real Google sign-in isn't
/// feasible, and Google always supplies a complete profile anyway), this provider is Huia itself, so both the
/// complete-profile auto-provisioning path and the missing-claims confirmation path can be scripted
/// end-to-end and asserted all the way through to a completed authorization code.
/// </summary>
[Collection("Huia E2E")]
public sealed class ExternalIdentityServerLoginE2ETests(HuiaAppFixture fixture) : IAsyncLifetime
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

    // One line over MA0051's default 60-line limit; splitting this single linear browser script wouldn't
    // reduce its actual complexity, just spread it across an extra method.
#pragma warning disable MA0051
    [Fact]
    public async Task ExternalLogin_ViaIdentityServer_CreatesAccountAndCompletesTheOriginalAuthorization()
    {
        var apiBaseUrl = GetBaseUrl("todoapi").TrimEnd('/');
        var identityServerBaseUrl = GetBaseUrl("identityserver").TrimEnd('/');
        var email = $"e2e-idp-{Guid.NewGuid():N}@example.com";
        const string password = "P@ssw0rd123!";

        // Start an authorization request for the existing "scalar" client (same one SecondClientSsoE2ETests
        // uses) while unauthenticated, so there's a real pending request to resume at the end — the same
        // returnUrl round-trips through every hop below (Login -> ExternalLogin challenge -> IdentityServer's
        // own authorize/login/register -> callback -> ExternalLoginConfirmation), and a broken hop anywhere
        // in that chain would either fail to redirect or land back on Huia's login/error page instead of
        // reaching scalar's redirect URI with a code.
        var query = string.Join('&', new[]
        {
            "response_type=code",
            "client_id=scalar",
            $"redirect_uri={Uri.EscapeDataString($"{apiBaseUrl}/scalar/v1")}",
            "scope=todos",
            "code_challenge=abc",
            "code_challenge_method=plain",
        });

        await _page.GotoAsync($"{apiBaseUrl}/connect/authorize?{query}");
        await _page.WaitForURLAsync(url => url.Contains("/identity/account/login", StringComparison.Ordinal),
            new PageWaitForURLOptions { Timeout = 30000 });

        // Exact: true — "Huia IdP (Partial Profile)" (see the sibling test below) also renders on this same
        // login page, and its accessible name contains "Huia IdP" as a substring, which Playwright's default
        // name matching would otherwise treat as a match too.
        var idpButton = _page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Huia IdP", Exact = true });
        await Assertions.Expect(idpButton).ToBeVisibleAsync(new() { Timeout = 30000 });

        await Task.WhenAll(
            _page.WaitForURLAsync(
                url => string.Equals(new Uri(url).Host, new Uri(identityServerBaseUrl).Host, StringComparison.Ordinal)
                       && url.Contains("/identity/account/login", StringComparison.Ordinal),
                new PageWaitForURLOptions { Timeout = 30000 }),
            idpButton.ClickAsync());

        // Not signed in to IdentityServer yet either — register a brand-new account there.
        await _page.GetByRole(AriaRole.Link, new() { Name = "Create an account" }).ClickAsync();

        await _page.GetByLabel("First name").FillAsync("E2E");
        await _page.GetByLabel("Last name").FillAsync("Idp");
        await _page.GetByLabel("Email").FillAsync(email);
        await _page.GetByLabel("Password", new() { Exact = true }).FillAsync(password);
        await _page.GetByLabel("Confirm password").FillAsync(password);

        // IdentityServer's own RequireConfirmedAccount is false (Huia's default), so registering signs the
        // user in immediately and resumes straight back through /connect/authorize to TodoApi's callback —
        // no separate "click sign in" step needed here.
        await Task.WhenAll(
            _page.WaitForURLAsync(
                url => string.Equals(new Uri(url).Host, new Uri(apiBaseUrl).Host, StringComparison.Ordinal),
                new PageWaitForURLOptions { Timeout = 30000 }),
            _page.GetByRole(AriaRole.Button, new() { Name = "Create account" }).ClickAsync());

        // IdentityServer never confirmed this brand-new account's email, so ExternalClaimsMapper.IsEmailVerified
        // is false and TodoApi's ExternalLogin handler falls through to the explicit-consent confirmation page
        // instead of auto-provisioning — see ExternalLoginConfirmation.cshtml.cs.
        await _page.WaitForURLAsync(
            url => url.Contains("/identity/account/externalloginconfirmation", StringComparison.OrdinalIgnoreCase),
            new PageWaitForURLOptions { Timeout = 30000 });

        // The name/email fields are pre-filled (and disabled) from the claims IdentityServer's own token
        // reported — proves the claims actually made it across the OIDC round trip, not just that some page
        // loaded.
        await Assertions.Expect(_page.GetByLabel("First name")).ToHaveValueAsync("E2E");
        await Assertions.Expect(_page.GetByLabel("Last name")).ToHaveValueAsync("Idp");
        await Assertions.Expect(_page.GetByLabel("Email")).ToHaveValueAsync(email);

        await _page.GetByRole(AriaRole.Button, new() { Name = "Create account" }).ClickAsync();

        // The preserved returnUrl resumes the original "scalar" authorization request and silently issues a
        // code — proof the whole chain (IdentityServer round trip + TodoApi account creation) completed, not
        // just that some page loaded along the way.
        await _page.WaitForURLAsync(
            url => url.Contains("/scalar/v1", StringComparison.Ordinal) && url.Contains("code=", StringComparison.Ordinal),
            new PageWaitForURLOptions { Timeout = 30000 });
    }
#pragma warning restore MA0051

    // One line over MA0051's default 60-line limit; splitting this single linear browser script wouldn't
    // reduce its actual complexity, just spread it across an extra method.
#pragma warning disable MA0051
    [Fact]
    public async Task ExternalLogin_ViaIdentityServerWithMissingNameClaims_RequiresManualEntryThenCompletes()
    {
        var apiBaseUrl = GetBaseUrl("todoapi").TrimEnd('/');
        var identityServerBaseUrl = GetBaseUrl("identityserver").TrimEnd('/');
        var email = $"e2e-idp-partial-{Guid.NewGuid():N}@example.com";
        const string password = "P@ssw0rd123!";

        var query = string.Join('&', new[]
        {
            "response_type=code",
            "client_id=scalar",
            $"redirect_uri={Uri.EscapeDataString($"{apiBaseUrl}/scalar/v1")}",
            "scope=todos",
            "code_challenge=abc",
            "code_challenge_method=plain",
        });

        await _page.GotoAsync($"{apiBaseUrl}/connect/authorize?{query}");
        await _page.WaitForURLAsync(url => url.Contains("/identity/account/login", StringComparison.Ordinal),
            new PageWaitForURLOptions { Timeout = 30000 });

        var idpButton = _page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Huia IdP (Partial Profile)" });
        await Assertions.Expect(idpButton).ToBeVisibleAsync(new() { Timeout = 30000 });

        await Task.WhenAll(
            _page.WaitForURLAsync(
                url => string.Equals(new Uri(url).Host, new Uri(identityServerBaseUrl).Host, StringComparison.Ordinal)
                       && url.Contains("/identity/account/login", StringComparison.Ordinal),
                new PageWaitForURLOptions { Timeout = 30000 }),
            idpButton.ClickAsync());

        // IdentityServer's own Register page always requires a first/last name (RegisterModel's InputModel is
        // the same one Register.cshtml uses) — the registered account genuinely has both. What's "missing" is
        // that huia-idp-partial's OpenIdConnect registration never asked for the "profile" scope those claims
        // live under, so TodoApi's side of this round trip never receives them regardless of what the account
        // actually has.
        await _page.GetByRole(AriaRole.Link, new() { Name = "Create an account" }).ClickAsync();

        await _page.GetByLabel("First name").FillAsync("Partial");
        await _page.GetByLabel("Last name").FillAsync("Profile");
        await _page.GetByLabel("Email").FillAsync(email);
        await _page.GetByLabel("Password", new() { Exact = true }).FillAsync(password);
        await _page.GetByLabel("Confirm password").FillAsync(password);

        await Task.WhenAll(
            _page.WaitForURLAsync(
                url => string.Equals(new Uri(url).Host, new Uri(apiBaseUrl).Host, StringComparison.Ordinal),
                new PageWaitForURLOptions { Timeout = 30000 }),
            _page.GetByRole(AriaRole.Button, new() { Name = "Create account" }).ClickAsync());

        // No verified email and no name claims at all this time — same ExternalLoginConfirmation page as the
        // full-profile test, but this is the branch that actually exercises it as a form the user has to fill
        // in themselves, rather than a page whose fields the user only ever sees pre-filled and disabled.
        await _page.WaitForURLAsync(
            url => url.Contains("/identity/account/externalloginconfirmation", StringComparison.OrdinalIgnoreCase),
            new PageWaitForURLOptions { Timeout = 30000 });

        var firstNameField = _page.GetByLabel("First name");
        var lastNameField = _page.GetByLabel("Last name");
        var emailField = _page.GetByLabel("Email");

        // Email came from the provider (the "email" scope is still requested) so it's pre-filled and
        // disabled, same as the full-profile test — the point of this test is specifically that the name
        // fields are not, since the provider never supplied them.
        await Assertions.Expect(emailField).ToHaveValueAsync(email);
        await Assertions.Expect(emailField).ToBeDisabledAsync();
        await Assertions.Expect(firstNameField).ToHaveValueAsync(string.Empty);
        await Assertions.Expect(firstNameField).ToBeEditableAsync();
        await Assertions.Expect(lastNameField).ToHaveValueAsync(string.Empty);
        await Assertions.Expect(lastNameField).ToBeEditableAsync();

        // Submitting with the name fields still blank should fail client-side validation and keep the user on
        // this page — proves the [Required] validation on these two fields is actually still enforced now
        // that they're editable, not just decorative markup left over from the disabled case. There's nothing
        // to WaitForURLAsync on here: a client-side validation block never issues a request or navigates, so
        // this instead gives aspnet-validation a moment to run and then confirms the URL never moved.
        await _page.GetByRole(AriaRole.Button, new() { Name = "Create account" }).ClickAsync();
        await _page.WaitForTimeoutAsync(500);
        Assert.Contains("/identity/account/externalloginconfirmation", _page.Url, StringComparison.OrdinalIgnoreCase);

        await firstNameField.FillAsync("Partial");
        await lastNameField.FillAsync("Profile");

        await _page.GetByRole(AriaRole.Button, new() { Name = "Create account" }).ClickAsync();

        // Same proof as the full-profile test: the preserved returnUrl resumes the original "scalar"
        // authorization request and silently issues a code once the account is actually created.
        await _page.WaitForURLAsync(
            url => url.Contains("/scalar/v1", StringComparison.Ordinal) && url.Contains("code=", StringComparison.Ordinal),
            new PageWaitForURLOptions { Timeout = 30000 });
    }
#pragma warning restore MA0051

    private string GetBaseUrl(string resourceName)
    {
        using var client = fixture.App.CreateHttpClient(resourceName);
        return client.BaseAddress!.ToString();
    }
}
