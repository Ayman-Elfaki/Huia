using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Huia.Tests.Integration;


/// <summary>
/// Regression coverage for well-known web vulnerability classes in Huia's own Identity UI: open
/// redirect (CWE-601) through <c>returnUrl</c>, missing CSRF protection (CWE-352) on state-changing
/// form posts, and improper restriction of excessive authentication attempts (CWE-307). <see
/// cref="Huia.Tests.Unit.Helpers.ReturnUrlValidatorTests"/> already proves the DB-independent branches of
/// <c>ReturnUrlValidator.ResolveAsync</c> reject these payloads; this drives the real HTTP pipeline against a
/// real OpenIddict-backed application store instead, so it also catches a page that forgets to route its
/// returnUrl through it, and covers the branch that consults registered applications (see
/// <see cref="Login_Post_WithRegisteredClientHomeUriReturnUrl_AfterSuccessfulSignIn_RedirectsThere"/>).
/// </summary>
public class IdentityUiSecurityTests(TodoApiFactory factory) : IClassFixture<TodoApiFactory>
{
    private const string Password = "P@ssw0rd123!";

    public static TheoryData<string> OpenRedirectPayloads => new()
    {
        "https://evil.example/steal",
        "//evil.example",
        "/\\evil.example",
    };

    [Theory]
    [MemberData(nameof(OpenRedirectPayloads))]
    public async Task Login_Get_WhileAlreadyAuthenticated_WithMaliciousReturnUrl_FallsBackToRoot(string payload)
    {
        var client = CreateNonRedirectingClient();
        await IdentityUiTestHelpers.RegisterAsync(client, NewEmail(), Password);

        using var response = await client.GetAsync($"/identity/account/login?returnUrl={Uri.EscapeDataString(payload)}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }

    [Theory]
    [MemberData(nameof(OpenRedirectPayloads))]
    public async Task Register_Get_WhileAlreadyAuthenticated_WithMaliciousReturnUrl_FallsBackToRoot(string payload)
    {
        var client = CreateNonRedirectingClient();
        await IdentityUiTestHelpers.RegisterAsync(client, NewEmail(), Password);

        using var response = await client.GetAsync($"/identity/account/register?returnUrl={Uri.EscapeDataString(payload)}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }

    [Theory]
    [MemberData(nameof(OpenRedirectPayloads))]
    public async Task Login_Post_WithMaliciousReturnUrl_AfterSuccessfulSignIn_FallsBackToRoot(string payload)
    {
        var client = CreateNonRedirectingClient();
        var email = NewEmail();
        await IdentityUiTestHelpers.RegisterAsync(client, email, Password);
        await SignOutAsync(client);

        var loginUrl = "/identity/account/login";
        var token = await IdentityUiTestHelpers.GetAntiforgeryTokenAsync(client, loginUrl);

        using var response = await client.PostAsync(loginUrl, new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Input.Email"] = email,
            ["Input.Password"] = Password,
            ["returnUrl"] = payload,
            ["__RequestVerificationToken"] = token,
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }

    /// <summary>
    /// The counterpart to <see cref="Login_Post_WithMaliciousReturnUrl_AfterSuccessfulSignIn_FallsBackToRoot"/>:
    /// a returnUrl naming a registered client's own <c>HomeUri</c> (an origin Huia itself hands out — see
    /// <c>ClientHomeResolver</c>, used e.g. by ConfirmEmail's "Sign in" link) isn't an open-redirect payload
    /// and must survive the redirect after sign-in, not be dropped to "/" alongside genuinely malicious ones.
    /// </summary>
    [Fact]
    public async Task Login_Post_WithRegisteredClientHomeUriReturnUrl_AfterSuccessfulSignIn_RedirectsThere()
    {
        var client = CreateNonRedirectingClient();
        var email = NewEmail();
        await IdentityUiTestHelpers.RegisterAsync(client, email, Password);
        await SignOutAsync(client);

        const string homeUri = "http://localhost:3000";
        var loginUrl = "/identity/account/login";
        var token = await IdentityUiTestHelpers.GetAntiforgeryTokenAsync(client, loginUrl);

        using var response = await client.PostAsync(loginUrl, new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Input.Email"] = email,
            ["Input.Password"] = Password,
            ["returnUrl"] = homeUri,
            ["__RequestVerificationToken"] = token,
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(homeUri, response.Headers.Location?.OriginalString);
    }

    [Theory]
    [MemberData(nameof(OpenRedirectPayloads))]
    public async Task Logout_Post_WithMaliciousReturnUrl_FallsBackToRoot(string payload)
    {
        var client = CreateNonRedirectingClient();
        await IdentityUiTestHelpers.RegisterAsync(client, NewEmail(), Password);

        var logoutUrl = $"/identity/account/logout?returnUrl={Uri.EscapeDataString(payload)}";
        // Login would redirect (already authenticated at this point) rather than render a form to scrape a
        // token from; ForgotPassword doesn't check auth state, so it works regardless.
        var token = await IdentityUiTestHelpers.GetAntiforgeryTokenAsync(client, "/identity/account/forgotpassword");

        using var response = await client.PostAsync(logoutUrl, new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = token,
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Register_Post_WithoutAntiforgeryToken_IsRejected()
    {
        var client = CreateNonRedirectingClient();

        // Visiting the page first establishes the antiforgery cookie, so this proves the missing *form
        // field* is what's rejected, not simply the absence of any prior request.
        await client.GetAsync("/identity/account/register");

        using var response = await client.PostAsync("/identity/account/register", new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Input.FirstName"] = "Test",
            ["Input.LastName"] = "User",
            ["Input.Email"] = NewEmail(),
            ["Input.Password"] = Password,
            ["Input.ConfirmPassword"] = Password,
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("password")] // all-lowercase, no digit or symbol
    [InlineData("12345678")] // digits only
    [InlineData("Passw0rd")] // missing a non-alphanumeric character
    [InlineData("Ab1!")] // meets every character class but under the minimum length
    public async Task Register_Post_WithWeakPassword_IsRejected(string weakPassword)
    {
        var client = CreateNonRedirectingClient();

        using var response = await IdentityUiTestHelpers.RegisterAsync(client, NewEmail(), weakPassword);

        // A rejected registration re-renders the form (200), rather than the 302 a successful one issues.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_Post_WithoutAntiforgeryToken_IsRejected()
    {
        var client = CreateNonRedirectingClient();
        var email = NewEmail();
        await IdentityUiTestHelpers.RegisterAsync(client, email, Password);
        await SignOutAsync(client);

        await client.GetAsync("/identity/account/login");

        using var response = await client.PostAsync("/identity/account/login", new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Input.Email"] = email,
            ["Input.Password"] = Password,
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_Post_AfterFiveFailedAttempts_LocksOutEvenWithCorrectPassword()
    {
        var client = CreateNonRedirectingClient();
        var email = NewEmail();
        await IdentityUiTestHelpers.RegisterAsync(client, email, Password);
        await SignOutAsync(client);

        const string loginUrl = "/identity/account/login";

        // ASP.NET Core Identity's default MaxFailedAccessAttempts is 5; PasswordSignInAsync is called with
        // lockoutOnFailure: true (Login.cshtml.cs), so the 5th bad attempt itself already trips the lockout
        // (AccessFailedCount reaching the threshold locks the account and returns LockedOut on that same call).
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var badToken = await IdentityUiTestHelpers.GetAntiforgeryTokenAsync(client, loginUrl);
            using var failed = await client.PostAsync(loginUrl, new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Input.Email"] = email,
                ["Input.Password"] = "definitely-wrong-password",
                ["__RequestVerificationToken"] = badToken,
            }));
        }

        var token = await IdentityUiTestHelpers.GetAntiforgeryTokenAsync(client, loginUrl);
        using var response = await client.PostAsync(loginUrl, new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Input.Email"] = email,
            ["Input.Password"] = Password,
            ["__RequestVerificationToken"] = token,
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/identity/account/lockout", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Login_Post_Success_IssuesHttpOnlyCookie()
    {
        var client = CreateNonRedirectingClient();

        using var response = await IdentityUiTestHelpers.RegisterAsync(client, NewEmail(), Password);

        var identityCookie = response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.FirstOrDefault(c => c.Contains("Identity.Application", StringComparison.Ordinal))
            : null;

        Assert.NotNull(identityCookie);
        Assert.Contains("httponly", identityCookie, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The cookie authentication handler's own <c>AccessDeniedPath</c> redirect appends the original
    /// destination as <c>?ReturnUrl=</c> automatically — <c>AccessDeniedModel</c> used to ignore it entirely,
    /// so its "back to sign in" link always sent the user to Huia's own bare root regardless of what they'd
    /// actually been trying to reach (including an in-progress OAuth flow, if that's what triggered the
    /// access-denied response).
    /// </summary>
    [Fact]
    public async Task AccessDenied_Get_WithReturnUrl_ThreadsItThroughToTheSignInLink()
    {
        var client = CreateNonRedirectingClient();

        using var response = await client.GetAsync("/identity/account/accessdenied?ReturnUrl=%2Fapi%2Ftodos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("returnUrl=%2Fapi%2Ftodos", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_Get_SetsClickjackingProtectionHeader()
    {
        var client = CreateNonRedirectingClient();

        using var response = await client.GetAsync("/identity/account/login");

        Assert.True(response.Headers.TryGetValues("X-Frame-Options", out var values));
        Assert.Contains(values!, v => string.Equals(v, "SAMEORIGIN", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task SignOutAsync(HttpClient client)
    {
        // Login would redirect (already authenticated) rather than render a form to scrape a token from;
        // ForgotPassword doesn't check auth state, so it works regardless of whether we're signed in yet.
        var token = await IdentityUiTestHelpers.GetAntiforgeryTokenAsync(client, "/identity/account/forgotpassword");
        using var response = await client.PostAsync("/identity/account/logout", new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = token,
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private HttpClient CreateNonRedirectingClient() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost"),
    });

    private static string NewEmail() => $"exploit-test-{Guid.NewGuid():N}@example.com";
}
