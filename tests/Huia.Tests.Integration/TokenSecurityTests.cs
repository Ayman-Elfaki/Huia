using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Validation;

namespace Huia.Tests.Integration;


/// <summary>
/// Companion to <see cref="IdentityUiSecurityTests"/>, covering the OAuth/OIDC surface: bearer-token
/// tampering against a protected resource, PKCE (RFC 7636) enforcement on the "scalar" sample client's
/// authorization_code flow — a public client, so PKCE is its only defense against a stolen authorization
/// code being redeemed by someone other than the party that started the flow — and the redirect/client
/// binding checks that mature OIDC providers are hardened against: unregistered
/// <c>redirect_uri</c>/<c>post_logout_redirect_uri</c> values (open redirect at the protocol level, distinct
/// from the Identity UI's own <c>returnUrl</c> covered by <see cref="IdentityUiSecurityTests"/>), an
/// authorization code being redeemed by a client it wasn't issued to (confused-deputy/token substitution),
/// well-known authority-confusion and path-traversal redirect_uri bypass primitives (Huia has no wildcard
/// redirect URI support for those bypasses' root cause to apply to, but the bypass strings themselves are
/// still worth proving rejected), the deprecated implicit
/// grant, calling <c>/connect/userinfo</c> with no token, refresh token rotation/reuse-rejection,
/// algorithm-confusion and <c>alg: none</c> token forgery, a scope-less-but-otherwise-valid token being
/// turned away by scope/audience enforcement, the absence of an exploitable Dynamic Client Registration
/// endpoint, and <c>state</c> parameter round-tripping (the CSRF defense OAuth clients rely on the
/// authorization server to preserve). These are all enforced by OpenIddict itself rather than Huia's own
/// endpoint code, but nothing in this repo proved it before — this locks that contract in as a regression
/// test.
/// </summary>
public class TokenSecurityTests(TodoApiFactory factory) : IClassFixture<TodoApiFactory>
{
    private const string Password = "P@ssw0rd123!";
    private const string ClientId = "scalar";

    // Dev-fallback credentials for the sample's confidential "todo-web" client (Huia.TodoApi/Program.cs);
    // used only to prove an authorization code minted for "scalar" can't be redeemed as this other client.
    private const string OtherClientId = "todo-web";
    private const string OtherClientSecret = "todo-web-dev-secret";
    private const string OtherClientRedirectUri = "http://localhost:3000/api/auth/callback/huia";

    [Fact]
    public async Task Authorize_WithUnregisteredRedirectUri_IsRejected()
    {
        var (client, _) = await CreateSignedInClientAsync();

        using var response = await AuthorizeAsync(client, "https://evil.example/steal");

        // Must not redirect to the attacker's URL — a redirect at all here would be the open-redirect bug.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    /// <summary>
    /// A known authority-confusion bypass class (malformed authority component with an embedded "@"
    /// bypassing wildcard redirect URI matching): a redirect_uri whose *prefix* is the registered exact URI,
    /// but whose actual host — the part after "@" — is attacker-controlled. Huia's redirect URIs are plain
    /// exact-match strings with no wildcard support (see <c>ClientApplicationOptions.AddRedirectUri</c>), so
    /// this bypass class doesn't apply here, but it's worth proving the prefix trick itself still fails.
    /// </summary>
    [Fact]
    public async Task Authorize_WithAuthorityConfusionRedirectUri_IsRejected()
    {
        var (client, redirectUri) = await CreateSignedInClientAsync();

        using var response = await AuthorizeAsync(client, redirectUri + "@evil.example");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    /// <summary>A known bypass class: path traversal defeating a wildcard-restricted redirect path.</summary>
    [Fact]
    public async Task Authorize_WithPathTraversalRedirectUri_IsRejected()
    {
        var (client, redirectUri) = await CreateSignedInClientAsync();

        using var response = await AuthorizeAsync(client, redirectUri + "/..;/evil");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    /// <summary>
    /// The implicit grant (tokens returned directly in the redirect fragment, no client authentication)
    /// is deprecated by the OAuth 2.0 Security BCP precisely because it exposes tokens to the browser
    /// history/referrer/logs; none of Huia's client descriptors grant the Implicit permission, so
    /// requesting it should be rejected outright rather than silently downgraded or ignored.
    /// </summary>
    [Fact]
    public async Task Authorize_WithImplicitGrantResponseType_IsRejected()
    {
        var (client, redirectUri) = await CreateSignedInClientAsync();

        var query = $"response_type=token&client_id={Uri.EscapeDataString(ClientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}&scope=todos";
        using var response = await client.GetAsync($"/connect/authorize?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Userinfo_WithoutToken_ReturnsUnauthorized()
    {
        var client = factory.CreateClient();

        using var response = await client.GetAsync("/connect/userinfo");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_WithUnregisteredPostLogoutRedirectUri_IsRejected()
    {
        var (client, _) = await CreateSignedInClientAsync();

        using var response = await client.GetAsync(
            $"/connect/logout?post_logout_redirect_uri={Uri.EscapeDataString("https://evil.example/pwned")}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task AuthorizationCode_RedeemedByDifferentClient_IsRejected()
    {
        var (client, redirectUri) = await CreateSignedInClientAsync();
        var (_, challenge) = CreatePkcePair();
        var code = await RequestAuthorizationCodeAsync(client, redirectUri, challenge);

        // "scalar" minted this code; try to redeem it as the unrelated confidential "todo-web" client instead
        // (a different client_id, a real client secret, and that client's own registered redirect_uri).
        using var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = OtherClientRedirectUri,
            ["client_id"] = OtherClientId,
            ["client_secret"] = OtherClientSecret,
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetTodos_WithTamperedSignature_ReturnsUnauthorized()
    {
        var client = factory.CreateClient();
        var token = await OAuthTestHelpers.GetTodoTestsAccessTokenAsync(client);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TamperSignature(token));

        using var response = await client.GetAsync("/api/todos");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetTodos_WithMalformedToken_ReturnsUnauthorized()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-real-token");

        using var response = await client.GetAsync("/api/todos");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// The classic RS256-to-HS256 key-confusion attack: a JWT library that picks its verification
    /// algorithm from the attacker-controlled <c>alg</c> header, rather than from server-side
    /// configuration, can be tricked into verifying an HMAC-SHA256 signature using the server's own
    /// *public* signing key (fetched from its own JWKS endpoint) as the shared secret — something only the
    /// attacker needs to know how to compute, since RSA public keys are, well, public. Huia's actual
    /// defense here isn't algorithm allow-listing so much as structural: access tokens are encrypted
    /// (JWE) end-to-end (<c>ServiceCollectionExtensions.cs</c> never calls
    /// <c>DisableAccessTokenEncryption()</c> unless <c>Server.AccessTokenEncryptionDisabled</c> is set),
    /// so a self-signed plaintext JWT — regardless of which algorithm it claims — never matches the
    /// expected wire format at all and is rejected before any algorithm/signature is even considered.
    /// </summary>
    [Fact]
    public async Task GetTodos_WithAlgorithmConfusionToken_ReturnsUnauthorized()
    {
        var client = factory.CreateClient();
        var signingKey = await GetJwksSigningKeyAsync(client);

        var modulusBytes = Base64UrlDecode(signingKey.GetProperty("n").GetString()!);
        var forgedHeader = Base64UrlEncode(Encoding.UTF8.GetBytes("""{"alg":"HS256","typ":"JWT"}"""));
        var forgedPayload = Base64UrlEncode(Encoding.UTF8.GetBytes(
            """{"sub":"attacker-controlled-user-id","scope":"todos"}"""));
        var signingInput = $"{forgedHeader}.{forgedPayload}";

        using var hmac = new HMACSHA256(modulusBytes);
        var forgedSignature = Base64UrlEncode(hmac.ComputeHash(Encoding.ASCII.GetBytes(signingInput)));
        var forgedToken = $"{signingInput}.{forgedSignature}";

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", forgedToken);
        using var response = await client.GetAsync("/api/todos");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// A JWT with <c>alg: none</c> and no signature segment at all, carrying an attacker-chosen <c>sub</c>
    /// claim — the payload a vulnerable library would happily decode and trust unverified. Same underlying
    /// defense as <see cref="GetTodos_WithAlgorithmConfusionToken_ReturnsUnauthorized"/>: the token doesn't
    /// match Huia's expected encrypted format, so it's rejected regardless of what its header claims.
    /// </summary>
    [Fact]
    public async Task GetTodos_WithNoneAlgorithmToken_ReturnsUnauthorized()
    {
        var client = factory.CreateClient();

        var forgedHeader = Base64UrlEncode(Encoding.UTF8.GetBytes("""{"alg":"none","typ":"JWT"}"""));
        var forgedPayload = Base64UrlEncode(Encoding.UTF8.GetBytes(
            """{"sub":"attacker-controlled-user-id","scope":"todos"}"""));
        var forgedToken = $"{forgedHeader}.{forgedPayload}.";

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", forgedToken);
        using var response = await client.GetAsync("/api/todos");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Missing audience/scope enforcement would let a token legitimately issued for one purpose reach a
    /// resource it was never meant to touch. Here, a real, validly-signed client_credentials token that
    /// requested no scope at all (so it carries no "todos" scope and, since "todos" is the only scope
    /// <c>Huia.TodoApi</c> seeds with a resource, no "todo-api" audience either) must still be turned away —
    /// proving neither check is just "any token from a known client will do". It's rejected at the
    /// validation/authentication layer now rather than reaching <c>RequireScope("todos")</c>'s authorization
    /// check at all: <c>Program.cs</c>'s <c>huia.Server.RequireAudiences("todo-api")</c> makes a missing
    /// audience fail token validation itself (401), which runs before authorization policies are even
    /// evaluated.
    /// </summary>
    [Fact]
    public async Task GetTodos_WithScopelessClientCredentialsToken_ReturnsUnauthorized()
    {
        var client = factory.CreateClient();
        using var tokenResponse = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "todo-tests",
            ["client_secret"] = "todo-tests-dev-secret",
            // No "scope" parameter at all.
        }));
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        using var document = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        var token = document.RootElement.GetProperty("access_token").GetString();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.GetAsync("/api/todos");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// <c>Huia.TodoApi/Program.cs</c> calls <c>huia.Server.RequireAudiences("todo-api")</c>; this proves that
    /// call actually reaches OpenIddict's validation options rather than being silently dropped — the
    /// behavioral check above (a token with no "todo-api" audience is rejected) already proves *something*
    /// enforces it, but not specifically that it's this configured value doing so.
    /// </summary>
    [Fact]
    public void ValidationOptions_RequireTheAudienceConfiguredInProgramCs()
    {
        using var scope = factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<OpenIddictValidationOptions>>().CurrentValue;

        Assert.Contains("todo-api", options.Audiences);
    }

    /// <summary>
    /// Dynamic Client Registration abuse requires a reachable DCR endpoint in the first place — Huia never
    /// calls OpenIddict's <c>SetClientRegistrationEndpointUris</c>, so there's no self-service client
    /// registration surface at all for an anonymous caller to abuse. This is a regression guard: if DCR
    /// support is ever added without deliberately deciding how (or whether) to gate it, this test starts
    /// failing and flags the decision.
    /// </summary>
    [Fact]
    public async Task DynamicClientRegistration_EndpointIsNotExposed()
    {
        var client = factory.CreateClient();

        using var response = await client.PostAsync("/connect/register", new FormUrlEncodedContent(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["client_name"] = "evil" }));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// CSRF protection via the OAuth <c>state</c> parameter is fundamentally the *client's* responsibility
    /// (generate an unguessable value, stash it, and check it comes back unchanged) — the authorization
    /// server's only obligation is to thread it through untouched so that check is even possible. This
    /// proves Huia does: a client-chosen state value survives the full authorize round trip unmodified.
    /// (OpenIddict also appends its own RFC 9207 <c>iss</c> parameter alongside it, a defense against
    /// "IdP mix-up" attacks in multi-IdP setups — a bonus the client should also verify, though that's
    /// outside Huia's control.)
    /// </summary>
    [Fact]
    public async Task Authorize_StateParameter_RoundTripsUnmodified()
    {
        var (client, redirectUri) = await CreateSignedInClientAsync();
        var expectedState = $"csrf-entropy-{Guid.NewGuid():N}";

        var query = string.Join('&', new[]
        {
            "response_type=code",
            $"client_id={Uri.EscapeDataString(ClientId)}",
            $"redirect_uri={Uri.EscapeDataString(redirectUri)}",
            "scope=todos",
            "code_challenge=abc",
            "code_challenge_method=plain",
            $"state={Uri.EscapeDataString(expectedState)}",
        });
        using var response = await client.GetAsync($"/connect/authorize?{query}");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(expectedState, ExtractQueryParameter(response.Headers.Location!, "state"));
    }

    private static async Task<JsonElement> GetJwksSigningKeyAsync(HttpClient client)
    {
        using var discoveryResponse = await client.GetAsync("/.well-known/openid-configuration");
        using var discoveryDocument = JsonDocument.Parse(await discoveryResponse.Content.ReadAsStringAsync());
        var jwksUri = discoveryDocument.RootElement.GetProperty("jwks_uri").GetString()!;

        using var jwksResponse = await client.GetAsync(jwksUri);
        using var jwks = JsonDocument.Parse(await jwksResponse.Content.ReadAsStringAsync());

        return jwks.RootElement.GetProperty("keys").EnumerateArray()
            .First(key => key.TryGetProperty("use", out var use) && string.Equals(use.GetString(), "sig", StringComparison.Ordinal))
            .Clone();
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Convert.FromBase64String(padded);
    }

    [Fact]
    public async Task AuthorizationCode_WithValidPkce_IssuesAccessToken()
    {
        var (client, redirectUri) = await CreateSignedInClientAsync();
        var (verifier, challenge) = CreatePkcePair();
        var code = await RequestAuthorizationCodeAsync(client, redirectUri, challenge);

        using var response = await ExchangeCodeAsync(client, code, redirectUri, verifier);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrEmpty(document.RootElement.GetProperty("access_token").GetString()));
    }

    [Fact]
    public async Task AuthorizationCode_WithoutCodeVerifier_IsRejected()
    {
        var (client, redirectUri) = await CreateSignedInClientAsync();
        var (_, challenge) = CreatePkcePair();
        var code = await RequestAuthorizationCodeAsync(client, redirectUri, challenge);

        using var response = await ExchangeCodeAsync(client, code, redirectUri, codeVerifier: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AuthorizationCode_WithWrongCodeVerifier_IsRejected()
    {
        var (client, redirectUri) = await CreateSignedInClientAsync();
        var (_, challenge) = CreatePkcePair();
        var (wrongVerifier, _) = CreatePkcePair();
        var code = await RequestAuthorizationCodeAsync(client, redirectUri, challenge);

        using var response = await ExchangeCodeAsync(client, code, redirectUri, wrongVerifier);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AuthorizationCode_Replayed_SecondExchangeIsRejected()
    {
        var (client, redirectUri) = await CreateSignedInClientAsync();
        var (verifier, challenge) = CreatePkcePair();
        var code = await RequestAuthorizationCodeAsync(client, redirectUri, challenge);

        using var first = await ExchangeCodeAsync(client, code, redirectUri, verifier);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var replay = await ExchangeCodeAsync(client, code, redirectUri, verifier);

        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
    }

    /// <summary>
    /// Rolling refresh tokens are on by default (OpenIddict's <c>DisableRollingRefreshTokens</c> defaults
    /// to <see langword="false"/>): each refresh redeems the presented token and issues a new one instead
    /// of reusing the same one until it expires. <c>RefreshTokenReuseLeeway</c> (default 30s) deliberately
    /// still accepts the just-redeemed token for a short window afterward, to tolerate a client firing
    /// concurrent refresh requests — <see cref="TodoApiFactory"/> zeroes it out for this whole test class
    /// (nothing else here depends on that grace window), so reuse is rejected immediately instead of
    /// needing a real 30-second sleep to observe it.
    /// </summary>
    [Fact]
    public async Task RefreshToken_RotatesAndRejectsReuseOutsideLeeway()
    {
        var (client, redirectUri) = await CreateSignedInClientAsync();
        var (verifier, challenge) = CreatePkcePair();
        using var authResponse = await AuthorizeAsync(client, redirectUri, challenge, scope: "todos offline_access");
        Assert.Equal(HttpStatusCode.Found, authResponse.StatusCode);
        var code = ExtractQueryParameter(authResponse.Headers.Location!, "code");

        using var tokenResponse = await ExchangeCodeAsync(client, code, redirectUri, verifier);
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        using var tokenDocument = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        var refreshToken = tokenDocument.RootElement.GetProperty("refresh_token").GetString()!;

        using var firstRefresh = await RefreshAsync(client, refreshToken);
        Assert.Equal(HttpStatusCode.OK, firstRefresh.StatusCode);

        // The original refresh token was just redeemed and rotated out; outside the (here, zeroed) leeway
        // window, reusing it must be rejected rather than silently honored again.
        using var replay = await RefreshAsync(client, refreshToken);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
    }

    /// <summary>Registers and signs in a fresh user, and looks up the "scalar" client's registered redirect URI.</summary>
    private async Task<(HttpClient Client, string RedirectUri)> CreateSignedInClientAsync()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
        await IdentityUiTestHelpers.RegisterAsync(client, $"pkce-test-{Guid.NewGuid():N}@example.com", Password);

        using var scope = factory.Services.CreateScope();
        var applicationManager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var application = await applicationManager.FindByClientIdAsync(ClientId)
            ?? throw new InvalidOperationException($"The '{ClientId}' sample client is not registered.");
        var redirectUri = (await applicationManager.GetRedirectUrisAsync(application)).First();

        return (client, redirectUri);
    }

    /// <summary>Drives the interactive GET /connect/authorize leg (auto-approved, no consent UI) and scrapes the issued code.</summary>
    private static async Task<string> RequestAuthorizationCodeAsync(HttpClient client, string redirectUri, string codeChallenge)
    {
        using var response = await AuthorizeAsync(client, redirectUri, codeChallenge);
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);

        var location = response.Headers.Location ?? throw new InvalidOperationException("No redirect Location header.");
        return ExtractQueryParameter(location, "code");
    }

    /// <summary>Issues the raw GET /connect/authorize request without asserting the outcome, so callers can probe rejections too.</summary>
    private static async Task<HttpResponseMessage> AuthorizeAsync(
        HttpClient client, string redirectUri, string? codeChallenge = null, string scope = "todos")
    {
        var (_, challenge) = codeChallenge is null ? CreatePkcePair() : (string.Empty, codeChallenge);
        var query = string.Join('&', new[]
        {
            "response_type=code",
            $"client_id={Uri.EscapeDataString(ClientId)}",
            $"redirect_uri={Uri.EscapeDataString(redirectUri)}",
            $"scope={Uri.EscapeDataString(scope)}",
            $"code_challenge={Uri.EscapeDataString(challenge)}",
            "code_challenge_method=S256",
            $"state={Guid.NewGuid():N}",
        });

        return await client.GetAsync($"/connect/authorize?{query}");
    }

    private static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string refreshToken) =>
        client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = ClientId,
        }));

    private static async Task<HttpResponseMessage> ExchangeCodeAsync(
        HttpClient client, string code, string redirectUri, string? codeVerifier)
    {
        var form = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = ClientId,
        };
        if (codeVerifier is not null)
        {
            form["code_verifier"] = codeVerifier;
        }

        return await client.PostAsync("/connect/token", new FormUrlEncodedContent(form));
    }

    private static string ExtractQueryParameter(Uri uri, string name)
    {
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (string.Equals(parts[0], name, StringComparison.Ordinal))
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        throw new InvalidOperationException($"Query parameter '{name}' not found in '{uri}'.");
    }

    private static (string Verifier, string Challenge) CreatePkcePair()
    {
        var verifier = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return (verifier, challenge);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Flips the first character of a JWT's signature segment, keeping its shape valid but its signature wrong.</summary>
    private static string TamperSignature(string jwt)
    {
        var parts = jwt.Split('.');
        var signature = parts[^1];
        parts[^1] = (signature[0] == 'A' ? 'B' : 'A') + signature[1..];
        return string.Join('.', parts);
    }
}
