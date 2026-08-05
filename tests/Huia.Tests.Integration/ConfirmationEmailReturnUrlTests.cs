using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;

namespace Huia.Tests.Integration;

/// <summary>
/// <c>Huia.TodoApi</c> doesn't require a confirmed account to sign in (<c>RequireConfirmedAccount = false</c>),
/// so registering signs the user in immediately and the confirmation email is a purely out-of-band,
/// click-whenever link. Register.cshtml.cs deliberately drops the original <c>/connect/authorize</c>
/// returnUrl rather than baking it into that email verbatim — replaying it later would fail the client's PKCE
/// check (the code_verifier cookie that started the flow is long gone by the time confirmation is optional
/// rather than gating).
/// But dropping it entirely used to mean the ConfirmEmail page's "Sign in" link sent the
/// user to Huia's own bare root instead of back to the client application they actually registered from.
/// These tests prove <c>ClientHomeResolver</c> fixes that: the confirmation link's returnUrl resolves to
/// the originating client's own registered redirect_uri origin instead of being dropped or replaying the
/// stale one-time authorize URL — covering both Register.cshtml.cs (which generates the link when an account
/// is first created) and ResendEmailConfirmation.cshtml.cs (the same link, regenerated on request for an
/// account that hasn't confirmed yet).
/// </summary>
public class ConfirmationEmailReturnUrlTests(TodoApiFactory factory) : IClassFixture<TodoApiFactory>
{
    private const string ClientId = "todo-web";

    [Fact]
    public async Task Register_ConfirmationLinkReturnUrl_ResolvesToTheOriginatingClientsOrigin()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        var (redirectUri, clientOrigin) = await GetClientRedirectUriAsync();
        var authorizeReturnUrl = await GetLoginReturnUrlAsync(client, redirectUri);

        var email = $"confirm-returnurl-test-{Guid.NewGuid():N}@example.com";
        using var response =
            await IdentityUiTestHelpers.RegisterAsync(client, email, "P@ssw0rd123!", authorizeReturnUrl);
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);

        var sent = Assert.Single(factory.SentConfirmationLinks, s => s.Email == email);
        var confirmationReturnUrl = ExtractQueryParameter(sent.ConfirmationLink, "returnUrl");

        Assert.Equal(clientOrigin, confirmationReturnUrl);
    }

    [Fact]
    public async Task Register_WithNoReturnUrlAtAll_ConfirmationLinkHasNoReturnUrl()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        var email = $"confirm-noreturnurl-test-{Guid.NewGuid():N}@example.com";
        using var response = await IdentityUiTestHelpers.RegisterAsync(client, email, "P@ssw0rd123!");
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);

        var sent = Assert.Single(factory.SentConfirmationLinks, s => s.Email == email);
        Assert.Null(ExtractQueryParameter(sent.ConfirmationLink, "returnUrl", throwIfMissing: false));
    }

    [Fact]
    public async Task ResendEmailConfirmation_ConfirmationLinkReturnUrl_ResolvesToTheOriginatingClientsOrigin()
    {
        var (redirectUri, clientOrigin) = await GetClientRedirectUriAsync();

        // A *separate*, still-unauthenticated client for this: registering below signs the user in, and
        // AuthorizationEndpoints skips straight past the login redirect for an already-signed-in caller — the
        // ReturnUrl this test needs only exists on that redirect for someone who isn't signed in yet.
        var anonymousClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
        var authorizeReturnUrl = await GetLoginReturnUrlAsync(anonymousClient, redirectUri);

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
        var email = $"resend-returnurl-test-{Guid.NewGuid():N}@example.com";

        // Register without a returnUrl at all (the account itself doesn't need one to exist) — what matters
        // is the *resend* request's own returnUrl, exercised separately below.
        using var registerResponse = await IdentityUiTestHelpers.RegisterAsync(client, email, "P@ssw0rd123!");
        Assert.Equal(HttpStatusCode.Found, registerResponse.StatusCode);
        var resendUrl = "/identity/account/resendemailconfirmation" +
                        $"?returnUrl={Uri.EscapeDataString(authorizeReturnUrl)}";
        var token = await IdentityUiTestHelpers.GetAntiforgeryTokenAsync(client, resendUrl);

        using var response = await client.PostAsync(resendUrl, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["returnUrl"] = authorizeReturnUrl,
            ["__RequestVerificationToken"] = token,
        }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Registering above already sent one confirmation link (with no returnUrl); this resend sends a
        // second, distinct one — the one under test here is that later one.
        var sent = factory.SentConfirmationLinks.Last(s => s.Email == email);
        var confirmationReturnUrl = ExtractQueryParameter(sent.ConfirmationLink, "returnUrl");

        Assert.Equal(clientOrigin, confirmationReturnUrl);
    }

    private async Task<(string RedirectUri, string Origin)> GetClientRedirectUriAsync()
    {
        using var scope = factory.Services.CreateScope();
        var applicationManager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var application = await applicationManager.FindByClientIdAsync(ClientId)
                          ?? throw new InvalidOperationException($"The '{ClientId}' sample client is not registered.");
        var redirectUri = (await applicationManager.GetRedirectUrisAsync(application)).First();

        return (redirectUri, new Uri(redirectUri).GetLeftPart(UriPartial.Authority));
    }

    /// <summary>Drives an unauthenticated GET /connect/authorize and scrapes the login redirect's ReturnUrl (the full authorize URL).</summary>
    private static async Task<string> GetLoginReturnUrlAsync(HttpClient client, string redirectUri)
    {
        var query = string.Join('&', new[]
        {
            "response_type=code",
            $"client_id={Uri.EscapeDataString(ClientId)}",
            $"redirect_uri={Uri.EscapeDataString(redirectUri)}",
            "scope=todos",
            "code_challenge=abc",
            "code_challenge_method=plain",
        });
        using var response = await client.GetAsync($"/connect/authorize?{query}");
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);

        return ExtractQueryParameter(response.Headers.Location!.ToString(), "ReturnUrl")
               ?? throw new InvalidOperationException("No ReturnUrl on the login redirect.");
    }

    /// <summary>Works on both relative (e.g. a Location header to Huia's own login page) and absolute URLs (e.g. a fully-qualified confirmation link).</summary>
    private static string? ExtractQueryParameter(string urlOrPath, string name, bool throwIfMissing = true)
    {
        var queryStart = urlOrPath.IndexOf('?');
        var query = QueryHelpers.ParseQuery(queryStart >= 0 ? urlOrPath[queryStart..] : "");

        if (query.TryGetValue(name, out var value))
        {
            return value.ToString();
        }

        return throwIfMissing
            ? throw new InvalidOperationException($"Query parameter '{name}' not found in '{urlOrPath}'.")
            : null;
    }
}