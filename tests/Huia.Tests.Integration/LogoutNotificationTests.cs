using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Huia.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;

namespace Huia.Tests.Integration;

/// <summary>
/// <c>/connect/logout</c> (RP-initiated logout) only ever handled the single client that sent the user
/// there — nothing told any <em>other</em> client with a live SSO session that the user had signed out
/// elsewhere. These tests drive a real sign-in against two of the sample's clients ("scalar" and
/// "todo-web", the latter configured in <c>Huia.TodoApi/Program.cs</c> with front/back-channel logout URIs)
/// and prove that logging out through one notifies the other: a front-channel iframe (with an <c>iss</c>
/// query parameter) for the browser to load, and a directly-POSTed, correctly-signed <c>logout_token</c>
/// for the back channel — while the client that itself initiated the logout is never notified of its own
/// logout, and the ordinary redirect-based logout flow for a client with no other live sessions is
/// unaffected.
/// </summary>
public class LogoutNotificationTests(TodoApiFactory factory) : IClassFixture<TodoApiFactory>
{
    private const string Password = "P@ssw0rd123!";
    private const string ScalarClientId = "scalar";
    private const string WebClientId = "todo-web";
    private const string WebClientSecret = "todo-web-dev-secret";
    private const string WebRedirectUri = "http://localhost:3000/api/auth/callback/huia";
    private const string WebPostLogoutRedirectUri = "http://localhost:3000";
    private const string WebFrontChannelLogoutUri = "http://localhost:3000/api/auth/frontchannel-logout";
    private const string WebBackChannelLogoutUri = "http://localhost:3000/api/auth/backchannel-logout";

    [Fact]
    public async Task Logout_ByClientWithNoOtherLiveSessions_StillRedirectsNormally()
    {
        var captured = new ConcurrentBag<CapturedRequest>();
        factory.BackChannelLogoutInterceptor = CaptureAsync(captured);
        using var client = CreateClient();

        await IdentityUiTestHelpers.RegisterAsync(client, $"logout-solo-{Guid.NewGuid():N}@example.com", Password);
        await SignInClientAsync(client);

        using var response = await client.GetAsync(
            $"/connect/logout?client_id={WebClientId}&post_logout_redirect_uri={Uri.EscapeDataString(WebPostLogoutRedirectUri)}&state=xyz");

        // todo-web is the only client with a live session and it's the one that initiated the logout, so it
        // must not be notified of its own logout — this is the same plain redirect OpenIddict always
        // produced here, unchanged by front/back-channel logout being layered on top for everyone else.
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.StartsWith(WebPostLogoutRedirectUri, response.Headers.Location!.ToString());
        Assert.Empty(captured);
    }

    [Fact]
    public async Task Logout_ByOtherClient_NotifiesLiveSession_ViaFrontAndBackChannel()
    {
        var captured = new ConcurrentBag<CapturedRequest>();
        factory.BackChannelLogoutInterceptor = CaptureAsync(captured);
        using var client = CreateClient();

        var email = $"logout-multi-{Guid.NewGuid():N}@example.com";
        await IdentityUiTestHelpers.RegisterAsync(client, email, Password);
        await SignInClientAsync(client);
        await SignInScalarAsync(client);

        // No client_id at all — "scalar" (the sample's public reference-page client) never registered a
        // post-logout redirect URI, so it has no EndSession permission and passing client_id=scalar here
        // would itself be rejected by OpenIddict before this test's own logic even runs. A bare RP-initiated
        // logout with no client_id is exactly what a browser hitting a "sign out everywhere" link would send.
        using var response = await client.GetAsync("/connect/logout");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var html = await response.Content.ReadAsStringAsync();

        var issuer = await GetIssuerAsync(client);
        var expectedIframeSrc = $"{WebFrontChannelLogoutUri}?iss={Uri.EscapeDataString(issuer)}";
        Assert.Contains(expectedIframeSrc, html);

        var request = Assert.Single(captured);
        Assert.Equal(WebBackChannelLogoutUri, request.Uri.ToString());

        const string prefix = "logout_token=";
        Assert.StartsWith(prefix, request.Body);
        var logoutToken = Uri.UnescapeDataString(request.Body[prefix.Length..]);
        var claims = await ValidateLogoutTokenAsync(client, logoutToken, issuer);

        Assert.Equal(WebClientId, claims.RootElement.GetProperty("aud").GetString());
        Assert.True(claims.RootElement.TryGetProperty("jti", out _));
        Assert.True(claims.RootElement.TryGetProperty("events", out var events));
        Assert.True(events.TryGetProperty("http://schemas.openid.net/event/backchannel-logout", out _));
        Assert.True(claims.RootElement.TryGetProperty("sid", out _));
        // Session-scoped, not subject-wide (see LogoutNotifier's own doc comment): a relying party keying
        // its own revocable session storage by "sub" instead of "sid" would revoke every device at once
        // instead of just the one that signed out, so "sub" is deliberately omitted from this token.
        Assert.False(claims.RootElement.TryGetProperty("sub", out _));

        // A second hit at the same URL (what the front-channel page's continuation script does once the
        // iframes above have had time to load) must not notify anyone a second time — the identity cookie
        // is already gone by then.
        using var secondResponse = await client.GetAsync("/connect/logout");
        Assert.Single(captured);
    }

    /// <summary>
    /// Session-scoping, not just multi-client notification: two genuinely separate sessions for the same
    /// user (two independent sign-ins, each in its own cookie container — "two browsers") each authorize
    /// "todo-web" independently (a fresh authorization per sign-in, not one shared/reused row — verified
    /// empirically, not assumed). Signing out session A must notify todo-web exactly once, for session A's
    /// own authorization — never twice. Two notifications would mean the old subject-wide lookup was still
    /// in effect (it never distinguished which session owns which authorization, so it would have reached
    /// session B's live one too); this is the regression the session-scoped rewrite of
    /// <see cref="LogoutNotifier"/> exists to prevent.
    /// </summary>
    [Fact]
    public async Task Logout_NotifiesOnlyForTheSigningOutSessionsOwnAuthorization_NotOtherLiveSessions()
    {
        var captured = new ConcurrentBag<CapturedRequest>();
        factory.BackChannelLogoutInterceptor = CaptureAsync(captured);

        var email = $"logout-session-scope-{Guid.NewGuid():N}@example.com";

        using var clientA = CreateClient();
        await IdentityUiTestHelpers.RegisterAsync(clientA, email, Password);
        await SignInClientAsync(clientA);

        using var clientB = CreateClient();
        await IdentityUiTestHelpers.SignInAsync(clientB, email, Password);
        await SignInClientAsync(clientB);

        using var responseA = await clientA.GetAsync("/connect/logout");
        Assert.Equal(HttpStatusCode.OK, responseA.StatusCode);

        var request = Assert.Single(captured);
        Assert.Equal(WebBackChannelLogoutUri, request.Uri.ToString());
    }

    private HttpClient CreateClient() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost"),
    });

    /// <summary>Records a captured back-channel logout POST (reading its body up front) and answers 200 OK, standing in for the actual relying party.</summary>
    private static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> CaptureAsync(
        ConcurrentBag<CapturedRequest> captured) =>
        async (request, cancellationToken) =>
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            captured.Add(new CapturedRequest(request.RequestUri!, body));
            return new HttpResponseMessage(HttpStatusCode.OK);
        };

    /// <summary>Drives the authorization-code + token exchange for the confidential "todo-web" client, establishing its live authorization.</summary>
    private static async Task SignInClientAsync(HttpClient client)
    {
        var state = Guid.NewGuid().ToString("N");
        var query = $"response_type=code&client_id={WebClientId}&redirect_uri={Uri.EscapeDataString(WebRedirectUri)}" +
                    $"&scope=todos&state={state}";
        using var authorizeResponse = await client.GetAsync($"/connect/authorize?{query}");
        Assert.Equal(HttpStatusCode.Found, authorizeResponse.StatusCode);
        var code = ExtractQueryParameter(authorizeResponse.Headers.Location!, "code");

        using var tokenResponse = await client.PostAsync("/connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = WebRedirectUri,
                ["client_id"] = WebClientId,
                ["client_secret"] = WebClientSecret,
            }));
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
    }

    /// <summary>Drives the authorization-code + PKCE + token exchange for the public "scalar" client, establishing its live authorization.</summary>
    private async Task SignInScalarAsync(HttpClient client)
    {
        using var scope = factory.Services.CreateScope();
        var applicationManager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var application = await applicationManager.FindByClientIdAsync(ScalarClientId)
                          ?? throw new InvalidOperationException(
                              $"The '{ScalarClientId}' sample client is not registered.");
        var redirectUri = (await applicationManager.GetRedirectUrisAsync(application)).First();

        var verifier = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64UrlEncode(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(verifier)));

        var query = $"response_type=code&client_id={ScalarClientId}&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                    $"&scope=todos&code_challenge={challenge}&code_challenge_method=S256&state={Guid.NewGuid():N}";
        using var authorizeResponse = await client.GetAsync($"/connect/authorize?{query}");
        Assert.Equal(HttpStatusCode.Found, authorizeResponse.StatusCode);
        var code = ExtractQueryParameter(authorizeResponse.Headers.Location!, "code");

        using var tokenResponse = await client.PostAsync("/connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
                ["client_id"] = ScalarClientId,
                ["code_verifier"] = verifier,
            }));
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
    }

    private static async Task<JsonDocument> ValidateLogoutTokenAsync(HttpClient client, string logoutToken,
        string issuer)
    {
        var signingKey = await GetJwksSigningKeyAsync(client);

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(logoutToken, new TokenValidationParameters
        {
            ValidIssuer = issuer,
            ValidAudience = WebClientId,
            IssuerSigningKey = signingKey,
        });

        Assert.True(result.IsValid, result.Exception?.ToString());

        var payload = logoutToken.Split('.')[1];
        return JsonDocument.Parse(Base64UrlDecode(payload));
    }

    private static async Task<string> GetIssuerAsync(HttpClient client)
    {
        using var discoveryResponse = await client.GetAsync("/.well-known/openid-configuration");
        using var discoveryDocument = JsonDocument.Parse(await discoveryResponse.Content.ReadAsStringAsync());
        return discoveryDocument.RootElement.GetProperty("issuer").GetString()!;
    }

    private static async Task<RsaSecurityKey> GetJwksSigningKeyAsync(HttpClient client)
    {
        using var discoveryResponse = await client.GetAsync("/.well-known/openid-configuration");
        using var discoveryDocument = JsonDocument.Parse(await discoveryResponse.Content.ReadAsStringAsync());
        var jwksUri = discoveryDocument.RootElement.GetProperty("jwks_uri").GetString()!;

        using var jwksResponse = await client.GetAsync(jwksUri);
        using var jwks = JsonDocument.Parse(await jwksResponse.Content.ReadAsStringAsync());
        var key = jwks.RootElement.GetProperty("keys").EnumerateArray()
            .First(k => k.TryGetProperty("use", out var use) && use.GetString() == "sig");

        var rsa = RSA.Create();
        rsa.ImportParameters(new RSAParameters
        {
            Modulus = Base64UrlDecode(key.GetProperty("n").GetString()!),
            Exponent = Base64UrlDecode(key.GetProperty("e").GetString()!),
        });

        return new RsaSecurityKey(rsa) { KeyId = key.GetProperty("kid").GetString() };
    }

    private static string ExtractQueryParameter(Uri uri, string name)
    {
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts[0] == name)
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        throw new InvalidOperationException($"Query parameter '{name}' not found in '{uri}'.");
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Convert.FromBase64String(padded);
    }

    /// <summary>A back-channel logout request captured by <see cref="CaptureAsync"/>, with its body read up front rather than left on the (disposable) <see cref="HttpRequestMessage"/> for later.</summary>
    private sealed record CapturedRequest(Uri Uri, string Body);
}