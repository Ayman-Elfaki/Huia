using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Huia.Identity;
using Huia.Sessions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;

namespace Huia.Tests.Integration;

/// <summary>
/// Proves Huia's session tracking end to end: a real sign-in creates a
/// <see cref="UserSessionDescriptor"/> and stamps its id on the <c>sid</c> claim of the resulting
/// identity token; a refresh carries that same <c>sid</c> forward unchanged; and — the single riskiest
/// assumption behind the whole feature — <see cref="AttachSessionAuthorizationHandler"/> really does tag
/// each OpenIddict authorization with the session that created it, so an admin revoking one session's
/// authorization leaves a <em>different</em> session's authorization (for the same user) untouched.
/// </summary>
public class SessionTests(TodoApiFactory factory) : IClassFixture<TodoApiFactory>
{
    private const string Password = "P@ssw0rd123!";
    private const string ScalarClientId = "scalar";
    private const string WebClientId = "todo-web";
    private const string WebClientSecret = "todo-web-dev-secret";
    private const string WebRedirectUri = "http://localhost:3000/api/auth/callback/huia";

    [Fact]
    public async Task SignIn_IdTokenCarriesSidClaim()
    {
        using var client = CreateClient();
        await IdentityUiTestHelpers.RegisterAsync(client, $"session-sid-{Guid.NewGuid():N}@example.com", Password);

        var (idToken, redirectUri) = await SignInScalarAsync(client, scope: "openid todos");

        var claims = DecodeJwtPayload(idToken);
        Assert.True(claims.RootElement.TryGetProperty(SessionClaimTypes.Sid, out var sid));
        Assert.False(string.IsNullOrWhiteSpace(sid.GetString()));
        _ = redirectUri;
    }

    [Fact]
    public async Task RefreshToken_CarriesSidForwardUnchanged()
    {
        using var client = CreateClient();
        await IdentityUiTestHelpers.RegisterAsync(client, $"session-refresh-{Guid.NewGuid():N}@example.com", Password);

        var (firstIdToken, refreshToken) =
            await SignInScalarForRefreshAsync(client, scope: "openid todos offline_access");
        var firstSid = DecodeJwtPayload(firstIdToken).RootElement.GetProperty(SessionClaimTypes.Sid).GetString();

        using var refreshResponse = await client.PostAsync("/connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = ScalarClientId,
            }));
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        using var refreshDocument = JsonDocument.Parse(await refreshResponse.Content.ReadAsStringAsync());
        var secondIdToken = refreshDocument.RootElement.GetProperty("id_token").GetString()!;
        var secondSid = DecodeJwtPayload(secondIdToken).RootElement.GetProperty(SessionClaimTypes.Sid).GetString();

        Assert.Equal(firstSid, secondSid);
    }

    /// <summary>
    /// The decisive end-to-end proof for the whole feature: two genuinely separate sessions for the same
    /// user (two independent sign-ins, each in its own cookie container — "two browsers") each authorize a
    /// different client. Revoking one session through the admin API must revoke only the OpenIddict
    /// authorization <see cref="AttachSessionAuthorizationHandler"/> tagged with that session's id, leaving
    /// the other session's authorization (and session record) untouched. This can only pass if that
    /// handler's dependency on running after OpenIddict's own <c>AttachAuthorization</c> handler — so that
    /// <c>GetAuthorizationId()</c> is already populated on the sign-in principal — actually holds at runtime.
    /// </summary>
    [Fact]
    public async Task AdminRevoke_EndsOnlyThatSessionsAuthorization_LeavesOtherSessionUntouched()
    {
        var email = $"session-scope-{Guid.NewGuid():N}@example.com";

        using var clientA = CreateClient();
        await IdentityUiTestHelpers.RegisterAsync(clientA, email, Password);
        var (idTokenA, _) = await SignInScalarAsync(clientA, scope: "openid todos");
        var claimsA = DecodeJwtPayload(idTokenA).RootElement;
        var sidA = claimsA.GetProperty(SessionClaimTypes.Sid).GetString()!;
        var subject = claimsA.GetProperty("sub").GetString()!;

        // A different application than session A's, so the two sessions can never collapse onto the same
        // OpenIddict authorization regardless of whatever reuse semantics OpenIddict applies.
        using var clientB = CreateClient();
        await IdentityUiTestHelpers.SignInAsync(clientB, email, Password);
        var idTokenB = await SignInWebClientAsync(clientB, scope: "openid todos");
        var sidB = DecodeJwtPayload(idTokenB).RootElement.GetProperty(SessionClaimTypes.Sid).GetString()!;

        Assert.NotEqual(sidA, sidB);

        using var adminClient = await CreateAdminBearerClientAsync();

        using var listResponse =
            await adminClient.GetAsync($"/api/identity/admin/sessions?subject={subject}&pageSize=100");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        using var listDocument = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        var ids = listDocument.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetString()).ToList();
        Assert.Contains(sidA, ids);
        Assert.Contains(sidB, ids);

        using var revokeResponse =
            await adminClient.PostAsync($"/api/identity/admin/sessions/{sidA}/revoke", content: null);
        Assert.Equal(HttpStatusCode.NoContent, revokeResponse.StatusCode);

        using var getA = await adminClient.GetAsync($"/api/identity/admin/sessions/{sidA}");
        using var getADocument = JsonDocument.Parse(await getA.Content.ReadAsStringAsync());
        Assert.NotEqual(JsonValueKind.Null, getADocument.RootElement.GetProperty("revokedAt").ValueKind);

        using var getB = await adminClient.GetAsync($"/api/identity/admin/sessions/{sidB}");
        using var getBDocument = JsonDocument.Parse(await getB.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, getBDocument.RootElement.GetProperty("revokedAt").ValueKind);

        using var scope = factory.Services.CreateScope();
        var authorizationManager = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
        var statusBySession = new Dictionary<string, string>();
        await foreach (var authorization in authorizationManager.FindBySubjectAsync(subject))
        {
            var properties = await authorizationManager.GetPropertiesAsync(authorization);
            if (properties.TryGetValue(SessionAuthorizationProperties.SessionId, out var value)
                && value.ValueKind == JsonValueKind.String)
            {
                statusBySession[value.GetString()!] = (await authorizationManager.GetStatusAsync(authorization))!;
            }
        }

        Assert.Equal(OpenIddictConstants.Statuses.Revoked, statusBySession[sidA]);
        Assert.Equal(OpenIddictConstants.Statuses.Valid, statusBySession[sidB]);
    }

    private HttpClient CreateClient() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost"),
    });

    /// <summary>Drives the authorization_code + PKCE + token exchange for the public "scalar" client, returning the id_token and the redirect URI used.</summary>
    private async Task<(string IdToken, string RedirectUri)> SignInScalarAsync(HttpClient client, string scope)
    {
        using var scopeHandle = factory.Services.CreateScope();
        var applicationManager = scopeHandle.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var application = await applicationManager.FindByClientIdAsync(ScalarClientId)
                          ?? throw new InvalidOperationException(
                              $"The '{ScalarClientId}' sample client is not registered.");
        var redirectUri = (await applicationManager.GetRedirectUrisAsync(application)).First();

        var (verifier, challenge) = CreatePkcePair();
        var query = $"response_type=code&client_id={ScalarClientId}&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                    $"&scope={Uri.EscapeDataString(scope)}&code_challenge={challenge}&code_challenge_method=S256&state={Guid.NewGuid():N}";
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
        using var tokenDocument = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        return (tokenDocument.RootElement.GetProperty("id_token").GetString()!, redirectUri);
    }

    /// <summary>Same as <see cref="SignInScalarAsync"/> but also returns the refresh token, for the refresh-carry-forward test.</summary>
    private async Task<(string IdToken, string RefreshToken)> SignInScalarForRefreshAsync(HttpClient client,
        string scope)
    {
        using var scopeHandle = factory.Services.CreateScope();
        var applicationManager = scopeHandle.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var application = await applicationManager.FindByClientIdAsync(ScalarClientId)
                          ?? throw new InvalidOperationException(
                              $"The '{ScalarClientId}' sample client is not registered.");
        var redirectUri = (await applicationManager.GetRedirectUrisAsync(application)).First();

        var (verifier, challenge) = CreatePkcePair();
        var query = $"response_type=code&client_id={ScalarClientId}&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                    $"&scope={Uri.EscapeDataString(scope)}&code_challenge={challenge}&code_challenge_method=S256&state={Guid.NewGuid():N}";
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
        using var tokenDocument = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        return (
            tokenDocument.RootElement.GetProperty("id_token").GetString()!,
            tokenDocument.RootElement.GetProperty("refresh_token").GetString()!);
    }

    /// <summary>Drives the authorization_code + token exchange for the confidential "todo-web" client, returning the id_token.</summary>
    private static async Task<string> SignInWebClientAsync(HttpClient client, string scope)
    {
        var state = Guid.NewGuid().ToString("N");
        var query = $"response_type=code&client_id={WebClientId}&redirect_uri={Uri.EscapeDataString(WebRedirectUri)}" +
                    $"&scope={Uri.EscapeDataString(scope)}&state={state}";
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
        using var tokenDocument = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        return tokenDocument.RootElement.GetProperty("id_token").GetString()!;
    }

    /// <summary>Registers and promotes a throwaway admin account, unrelated to whatever sessions the test itself is exercising, and returns a bearer-authorized client for the admin API.</summary>
    private async Task<HttpClient> CreateAdminBearerClientAsync()
    {
        var uiClient = CreateClient();
        var email = $"session-admin-{Guid.NewGuid():N}@example.com";
        await IdentityUiTestHelpers.RegisterAsync(uiClient, email, Password);

        using (var scope = factory.Services.CreateScope())
        {
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<HuiaRole>>();
            if (!await roleManager.RoleExistsAsync("Admin"))
            {
                await roleManager.CreateAsync(new HuiaRole { Name = "Admin" });
            }

            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<HuiaUser>>();
            var user = await userManager.FindByEmailAsync(email)
                       ?? throw new InvalidOperationException("Registered user not found.");
            await userManager.AddToRoleAsync(user, "Admin");
        }

        var query = $"response_type=code&client_id={WebClientId}&redirect_uri={Uri.EscapeDataString(WebRedirectUri)}" +
                    $"&scope={Uri.EscapeDataString("openid profile email todos roles")}&state={Guid.NewGuid():N}";
        using var authorizeResponse = await uiClient.GetAsync($"/connect/authorize?{query}");
        Assert.Equal(HttpStatusCode.Found, authorizeResponse.StatusCode);
        var code = ExtractQueryParameter(authorizeResponse.Headers.Location!, "code");

        using var tokenResponse = await uiClient.PostAsync("/connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = WebRedirectUri,
                ["client_id"] = WebClientId,
                ["client_secret"] = WebClientSecret,
            }));
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        using var tokenDocument = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        var accessToken = tokenDocument.RootElement.GetProperty("access_token").GetString()!;

        var apiClient = factory.CreateClient();
        apiClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return apiClient;
    }

    private static (string Verifier, string Challenge) CreatePkcePair()
    {
        var verifier = Base64UrlEncode(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var challenge =
            Base64UrlEncode(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(verifier)));
        return (verifier, challenge);
    }

    /// <summary>
    /// Decodes a JWT's payload segment without validating its signature —
    /// these tests are about claim propagation, not token-signing security
    /// (covered separately by <c>TokenSecurityTests</c>).
    /// </summary>
    private static JsonDocument DecodeJwtPayload(string jwt) => JsonDocument.Parse(Base64UrlDecode(jwt.Split('.')[1]));

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
}