using System.Net;
using Huia.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.Tests.Integration.ExternalLogin;

/// <summary>
/// End-to-end coverage for external (third-party) sign-in: the challenge/callback round trip driven against
/// <see cref="FakeIdentityProvider"/>, first-time account creation via the confirmation page, returning-user
/// direct sign-in, provider errors, email collisions, 2FA, and the Manage link/unlink endpoints.
/// </summary>
public class ExternalLoginTests(ExternalLoginTestFactory factory) : IClassFixture<ExternalLoginTestFactory>
{
    private const string Password = "P@ssw0rd123!";

    [Fact]
    public async Task NewIdentity_CompletesConfirmation_CreatesAccountAndSignsIn()
    {
        var email = $"external-new-{Guid.NewGuid():N}@example.com";
        const string picture = "https://example.com/avatar.png";
        factory.FakeIdentityProvider.NextIdentity = new FakeExternalIdentity(
            Subject: $"sub-{Guid.NewGuid():N}", email, "External", "User", EmailVerified: true, picture);

        using var client = factory.CreateClient();
        using var callbackResult = await DriveExternalSignInAsync(client);

        Assert.Equal(HttpStatusCode.Found, callbackResult.StatusCode);
        Assert.Contains("/identity/account/externalloginconfirmation",
            callbackResult.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);

        using var finalResponse = await CompleteConfirmationAsync(client, callbackResult.Headers.Location!, email);
        Assert.Equal(HttpStatusCode.Found, finalResponse.StatusCode);

        var user = await FindUserAsync(email);
        Assert.NotNull(user);
        Assert.True(user.EmailConfirmed);
        Assert.Equal(picture, user.Picture);

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<HuiaUser>>();
        var logins = await userManager.GetLoginsAsync(user);
        Assert.Single(logins);
        Assert.Equal(ExternalLoginTestFactory.ProviderScheme, logins[0].LoginProvider);
    }

    [Fact]
    public async Task AlreadyLinkedIdentity_SignsInDirectly_NoDuplicateAccountCreated()
    {
        var email = $"external-returning-{Guid.NewGuid():N}@example.com";
        var subject = $"sub-{Guid.NewGuid():N}";
        factory.FakeIdentityProvider.NextIdentity = new FakeExternalIdentity(subject, email, "External", "User",
            EmailVerified: true);

        using (var firstClient = factory.CreateClient())
        {
            using var firstCallback = await DriveExternalSignInAsync(firstClient);
            using var confirmResponse = await CompleteConfirmationAsync(firstClient, firstCallback.Headers.Location!, email);
            Assert.Equal(HttpStatusCode.Found, confirmResponse.StatusCode);
        }

        using var secondClient = factory.CreateClient();
        using var secondCallback = await DriveExternalSignInAsync(secondClient);

        // Signed straight in this time — no confirmation redirect, no second account.
        Assert.Equal(HttpStatusCode.Found, secondCallback.StatusCode);
        Assert.DoesNotContain("externalloginconfirmation", secondCallback.Headers.Location!.ToString(),
            StringComparison.OrdinalIgnoreCase);

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<HuiaUser>>();
        var user = await userManager.FindByLoginAsync(ExternalLoginTestFactory.ProviderScheme, subject);
        Assert.NotNull(user);
    }

    [Fact]
    public async Task ProviderReturnsError_RedirectsToLoginWithoutCreatingAccount()
    {
        factory.FakeIdentityProvider.SimulatedAuthorizeError = "access_denied";
        try
        {
            using var client = factory.CreateClient();
            using var callbackResult = await DriveExternalSignInAsync(client);

            Assert.Equal(HttpStatusCode.Found, callbackResult.StatusCode);
            Assert.Contains("/identity/account/login", callbackResult.Headers.Location!.ToString(),
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            factory.FakeIdentityProvider.SimulatedAuthorizeError = null;
        }
    }

    /// <summary>
    /// The sample enables <c>huia.EnableExternalLoginPasswordLinking()</c> (see <c>Huia.TodoApi/Program.cs</c>),
    /// so an email collision doesn't hard-error — it routes to <c>ExternalLoginLinkConfirmation</c> instead,
    /// and nothing is linked or created until the user actually proves ownership there (covered by the
    /// tests below). Never auto-links without that proof, regardless of the setting.
    /// </summary>
    [Fact]
    public async Task EmailCollidesWithExistingPasswordAccount_RoutesToLinkConfirmation_DoesNotAutoLink()
    {
        var email = $"external-collision-{Guid.NewGuid():N}@example.com";
        using var passwordClient = factory.CreateClient();
        await IdentityUiTestHelpers.RegisterAsync(passwordClient, email, Password);

        factory.FakeIdentityProvider.NextIdentity = new FakeExternalIdentity(
            $"sub-{Guid.NewGuid():N}", email, "External", "User", EmailVerified: true);

        using var externalClient = factory.CreateClient();
        using var callbackResult = await DriveExternalSignInAsync(externalClient);

        Assert.Equal(HttpStatusCode.Found, callbackResult.StatusCode);
        Assert.Contains("/identity/account/externalloginlinkconfirmation",
            callbackResult.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<HuiaUser>>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.NotNull(user);
        Assert.Empty(await userManager.GetLoginsAsync(user));
    }

    [Fact]
    public async Task LinkConfirmation_WrongPassword_DoesNotLinkAndAllowsRetry()
    {
        var email = $"external-linkwrong-{Guid.NewGuid():N}@example.com";
        using var passwordClient = factory.CreateClient();
        await IdentityUiTestHelpers.RegisterAsync(passwordClient, email, Password);

        var subject = $"sub-{Guid.NewGuid():N}";
        factory.FakeIdentityProvider.NextIdentity = new FakeExternalIdentity(subject, email, "External", "User",
            EmailVerified: true);

        using var client = factory.CreateClient();
        using var callbackResult = await DriveExternalSignInAsync(client);
        Assert.Contains("/identity/account/externalloginlinkconfirmation",
            callbackResult.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);

        var wrongAttempt = await SubmitLinkConfirmationAsync(client, callbackResult.Headers.Location!, "WrongPassword!1");
        Assert.Equal(HttpStatusCode.OK, wrongAttempt.StatusCode);
        Assert.Contains("Incorrect password", await wrongAttempt.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<HuiaUser>>();
        var user = await userManager.FindByEmailAsync(email) ?? throw new InvalidOperationException("User not found.");
        Assert.Empty(await userManager.GetLoginsAsync(user));

        // The external identity is still pending (not consumed by a failed attempt), so a correct retry on
        // the same session still succeeds.
        using var correctAttempt = await SubmitLinkConfirmationAsync(client, callbackResult.Headers.Location!, Password);
        Assert.Equal(HttpStatusCode.Found, correctAttempt.StatusCode);
        Assert.Single(await userManager.GetLoginsAsync(user));
    }

    [Fact]
    public async Task LinkConfirmation_CorrectPassword_LinksAndSignsIn()
    {
        var email = $"external-linkright-{Guid.NewGuid():N}@example.com";
        using var passwordClient = factory.CreateClient();
        await IdentityUiTestHelpers.RegisterAsync(passwordClient, email, Password);

        var subject = $"sub-{Guid.NewGuid():N}";
        factory.FakeIdentityProvider.NextIdentity = new FakeExternalIdentity(subject, email, "External", "User",
            EmailVerified: true);

        using var client = factory.CreateClient();
        using var callbackResult = await DriveExternalSignInAsync(client);

        using var linkResponse = await SubmitLinkConfirmationAsync(client, callbackResult.Headers.Location!, Password);
        Assert.Equal(HttpStatusCode.Found, linkResponse.StatusCode);

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<HuiaUser>>();
        var user = await userManager.FindByEmailAsync(email) ?? throw new InvalidOperationException("User not found.");
        var logins = await userManager.GetLoginsAsync(user);
        Assert.Single(logins);
        Assert.Equal(ExternalLoginTestFactory.ProviderScheme, logins[0].LoginProvider);

        // Now signed in — the app cookie is live, e.g. Login redirects away instead of rendering the form.
        using var loginPage = await client.GetAsync("/identity/account/login");
        Assert.Equal(HttpStatusCode.Found, loginPage.StatusCode);
    }

    [Fact]
    public async Task LinkConfirmation_TwoFactorEnabledAccount_LinksThenRequires2fa()
    {
        var email = $"external-link2fa-{Guid.NewGuid():N}@example.com";
        using var passwordClient = factory.CreateClient();
        await IdentityUiTestHelpers.RegisterAsync(passwordClient, email, Password);

        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<HuiaUser>>();
            var user = await userManager.FindByEmailAsync(email) ?? throw new InvalidOperationException("User not found.");
            // TwoFactorEnabled alone isn't enough — SignInManager only treats 2FA as actually required once
            // GetValidTwoFactorProvidersAsync is non-empty too, which needs a real authenticator key set.
            await userManager.ResetAuthenticatorKeyAsync(user);
            await userManager.SetTwoFactorEnabledAsync(user, true);
        }

        var subject = $"sub-{Guid.NewGuid():N}";
        factory.FakeIdentityProvider.NextIdentity = new FakeExternalIdentity(subject, email, "External", "User",
            EmailVerified: true);

        using var client = factory.CreateClient();
        using var callbackResult = await DriveExternalSignInAsync(client);

        using var linkResponse = await SubmitLinkConfirmationAsync(client, callbackResult.Headers.Location!, Password);
        Assert.Equal(HttpStatusCode.Found, linkResponse.StatusCode);
        Assert.Contains("/identity/account/loginwith2fa", linkResponse.Headers.Location!.ToString(),
            StringComparison.OrdinalIgnoreCase);

        // Linking already happened — proving the password was enough — even though the session itself still
        // needs the second factor to complete.
        using var scope2 = factory.Services.CreateScope();
        var userManager2 = scope2.ServiceProvider.GetRequiredService<UserManager<HuiaUser>>();
        var user2 = await userManager2.FindByEmailAsync(email) ?? throw new InvalidOperationException("User not found.");
        Assert.Single(await userManager2.GetLoginsAsync(user2));
    }

    [Fact]
    public async Task TwoFactorEnabledUser_ExternalSignInRedirectsToLoginWith2fa()
    {
        var email = $"external-2fa-{Guid.NewGuid():N}@example.com";
        var subject = $"sub-{Guid.NewGuid():N}";
        factory.FakeIdentityProvider.NextIdentity = new FakeExternalIdentity(subject, email, "External", "User",
            EmailVerified: true);

        using (var firstClient = factory.CreateClient())
        {
            using var firstCallback = await DriveExternalSignInAsync(firstClient);
            using var confirmResponse = await CompleteConfirmationAsync(firstClient, firstCallback.Headers.Location!, email);
            Assert.Equal(HttpStatusCode.Found, confirmResponse.StatusCode);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<HuiaUser>>();
            var user = await userManager.FindByLoginAsync(ExternalLoginTestFactory.ProviderScheme, subject)
                ?? throw new InvalidOperationException("User not found.");
            await userManager.SetTwoFactorEnabledAsync(user, true);
        }

        using var secondClient = factory.CreateClient();
        using var secondCallback = await DriveExternalSignInAsync(secondClient);

        Assert.Equal(HttpStatusCode.Found, secondCallback.StatusCode);
        Assert.Contains("/identity/account/loginwith2fa", secondCallback.Headers.Location!.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unlink_BlockedAsLastCredential_AllowedOncePasswordExists()
    {
        var email = $"external-unlink-{Guid.NewGuid():N}@example.com";
        var subject = $"sub-{Guid.NewGuid():N}";
        factory.FakeIdentityProvider.NextIdentity = new FakeExternalIdentity(subject, email, "External", "User",
            EmailVerified: true);

        using var client = factory.CreateClient();
        using var callbackResult = await DriveExternalSignInAsync(client);
        using var confirmResponse = await CompleteConfirmationAsync(client, callbackResult.Headers.Location!, email);
        Assert.Equal(HttpStatusCode.Found, confirmResponse.StatusCode);

        // The user has no password yet — this login is their only credential, so removing it must be blocked.
        using var blockedResponse =
            await client.DeleteAsync($"/api/identity/manage/external-logins/{ExternalLoginTestFactory.ProviderScheme}/{subject}");
        Assert.Equal(HttpStatusCode.BadRequest, blockedResponse.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<HuiaUser>>();
            var user = await userManager.FindByLoginAsync(ExternalLoginTestFactory.ProviderScheme, subject)
                ?? throw new InvalidOperationException("User not found.");
            await userManager.AddPasswordAsync(user, Password);
        }

        using var allowedResponse =
            await client.DeleteAsync($"/api/identity/manage/external-logins/{ExternalLoginTestFactory.ProviderScheme}/{subject}");
        Assert.Equal(HttpStatusCode.OK, allowedResponse.StatusCode);
    }

    /// <summary>
    /// Drives the challenge → fake-IdP authorize → callback round trip on <paramref name="client"/>, stopping
    /// at whatever redirect <c>ExternalLoginModel.OnGetCallbackAsync</c> produces (to a returnUrl, to
    /// <c>ExternalLoginConfirmation</c>, back to <c>Login</c> with an error, or to <c>LoginWith2fa</c>).
    /// </summary>
    private async Task<HttpResponseMessage> DriveExternalSignInAsync(HttpClient client, string? returnUrl = null)
    {
        const string loginUrl = "/identity/account/login";
        var token = await IdentityUiTestHelpers.GetAntiforgeryTokenAsync(client, loginUrl);

        var challengeUrl = $"/identity/account/externallogin?provider={ExternalLoginTestFactory.ProviderScheme}" +
                            (returnUrl is null ? "" : $"&returnUrl={Uri.EscapeDataString(returnUrl)}");

        using var challengeResponse = await client.PostAsync(challengeUrl,
            new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["__RequestVerificationToken"] = token,
            }));
        Assert.Equal(HttpStatusCode.Found, challengeResponse.StatusCode);

        using var fakeIdpClient = factory.FakeIdentityProvider.CreateClient();
        using var authorizeResponse = await fakeIdpClient.GetAsync(challengeResponse.Headers.Location);
        Assert.Equal(HttpStatusCode.Found, authorizeResponse.StatusCode);

        // Landing on the app's own CallbackPath is where the OAuth handler exchanges the code for a token
        // and fetches userinfo (both routed to the fake IdP via BackchannelHttpHandler), then redirects to
        // whatever ConfigureExternalAuthenticationProperties's redirectUrl was — ExternalLogin's own Callback
        // handler — which is the response this method actually returns.
        using var callbackPathResponse = await client.GetAsync(authorizeResponse.Headers.Location);
        Assert.Equal(HttpStatusCode.Found, callbackPathResponse.StatusCode);

        return await client.GetAsync(callbackPathResponse.Headers.Location);
    }

    /// <summary>
    /// Submits <c>ExternalLoginConfirmation</c>'s form. <paramref name="email"/>/<paramref name="firstName"/>/
    /// <paramref name="lastName"/> should match what <see cref="FakeIdentityProvider.NextIdentity"/> was set
    /// to for this flow — <c>OnGetAsync</c> pre-fills the real form with exactly those values, so this is
    /// what an unmodified submit actually sends.
    /// </summary>
    private static async Task<HttpResponseMessage> CompleteConfirmationAsync(HttpClient client, Uri confirmationUrl,
        string email, string firstName = "External", string lastName = "User")
    {
        var token = await IdentityUiTestHelpers.GetAntiforgeryTokenAsync(client, confirmationUrl.ToString());

        return await client.PostAsync(confirmationUrl,
            new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Input.Email"] = email,
                ["Input.FirstName"] = firstName,
                ["Input.LastName"] = lastName,
                ["__RequestVerificationToken"] = token,
            }));
    }

    /// <summary>Submits <c>ExternalLoginLinkConfirmation</c>'s form with the given password.</summary>
    private static async Task<HttpResponseMessage> SubmitLinkConfirmationAsync(HttpClient client, Uri confirmationUrl,
        string password)
    {
        var token = await IdentityUiTestHelpers.GetAntiforgeryTokenAsync(client, confirmationUrl.ToString());

        return await client.PostAsync(confirmationUrl,
            new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Input.Password"] = password,
                ["__RequestVerificationToken"] = token,
            }));
    }

    private async Task<HuiaUser?> FindUserAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<HuiaUser>>();
        return await userManager.FindByEmailAsync(email);
    }
}
