using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.IdentityModel.Tokens;

namespace Huia.IntegrationTests.Infrastructure;

/// <summary>
/// Drives an interactive authorization-code + PKCE sign-in through the Razor account UI end to end,
/// returning the token-endpoint response. Each instance uses its own cookie-isolated client.
/// </summary>
public sealed partial class AuthCodeFlow(HuiaTestHost host, string tenant, string clientId, string redirectUri)
{
    private readonly HttpClient _client = host.CreateClient();

    /// <summary>The PKCE verifier used for this flow.</summary>
    public string CodeVerifier { get; } = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    /// <summary>Runs the whole flow with the given credentials. Returns the parsed token response.</summary>
    /// <param name="userName">The email / username to sign in with.</param>
    /// <param name="password">The password.</param>
    /// <param name="scope">The requested scope.</param>
    /// <returns>The token endpoint's JSON payload.</returns>
    public async Task<JsonDocument> SignInAsync(string userName, string password, string scope = "openid profile email offline_access")
    {
        var challenge = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(CodeVerifier)));
        var state = Guid.NewGuid().ToString("N");
        var authorizeUrl =
            $"/{tenant}/connect/authorize?response_type=code&client_id={Uri.EscapeDataString(clientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}&scope={Uri.EscapeDataString(scope)}" +
            $"&code_challenge={challenge}&code_challenge_method=S256&state={state}";

        var toLogin = await _client.GetAsync(authorizeUrl);
        toLogin.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var loginUrl = toLogin.Headers.Location!.ToString();
        loginUrl.ShouldContain("/identity/account/login");

        var loginPage = await _client.GetStringAsync(loginUrl);
        var antiforgery = ExtractAntiforgeryToken(loginPage);

        var postLogin = await _client.PostAsync(loginUrl, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = userName,
            ["Input.Password"] = password,
            ["Input.RememberMe"] = "false",
            ["__RequestVerificationToken"] = antiforgery,
        }));

        postLogin.StatusCode.ShouldBe(HttpStatusCode.Redirect, await SafeBody(postLogin));
        var backToAuthorize = postLogin.Headers.Location!.ToString();

        var toCallback = await _client.GetAsync(MakeAbsoluteLocal(backToAuthorize));
        toCallback.StatusCode.ShouldBe(HttpStatusCode.Redirect, await SafeBody(toCallback));
        var callback = toCallback.Headers.Location!.ToString();
        callback.ShouldStartWith(redirectUri);

        var code = ExtractQueryValue(callback, "code");
        code.ShouldNotBeNullOrEmpty();

        var tokenResponse = await _client.PostAsync($"/{tenant}/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code!,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = clientId,
            ["code_verifier"] = CodeVerifier,
        }));

        var body = await tokenResponse.Content.ReadAsStringAsync();
        tokenResponse.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body);
    }

    /// <summary>Exchanges a refresh token for a fresh token set.</summary>
    /// <param name="refreshToken">The refresh token.</param>
    /// <returns>The token endpoint's JSON payload.</returns>
    public async Task<JsonDocument> RefreshAsync(string refreshToken)
    {
        var response = await _client.PostAsync($"/{tenant}/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId,
        }));

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body);
    }

    /// <summary>Attempts a sign-in expected to fail; returns the re-rendered login page HTML.</summary>
    /// <param name="userName">The email / username.</param>
    /// <param name="password">The (wrong) password.</param>
    /// <returns>The login page HTML after the failed POST.</returns>
    public async Task<string> AttemptFailedSignInAsync(string userName, string password)
    {
        var authorizeUrl =
            $"/{tenant}/connect/authorize?response_type=code&client_id={Uri.EscapeDataString(clientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}&scope=openid&code_challenge=x&code_challenge_method=plain&state=s";

        var toLogin = await _client.GetAsync(authorizeUrl);
        var loginUrl = toLogin.Headers.Location!.ToString();
        var loginPage = await _client.GetStringAsync(loginUrl);
        var antiforgery = ExtractAntiforgeryToken(loginPage);

        var postLogin = await _client.PostAsync(loginUrl, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = userName,
            ["Input.Password"] = password,
            ["__RequestVerificationToken"] = antiforgery,
        }));

        return await postLogin.Content.ReadAsStringAsync();
    }

    private string MakeAbsoluteLocal(string location) =>
        location.StartsWith('/') ? location : new Uri(new Uri("http://localhost"), location).PathAndQuery;

    private static async Task<string> SafeBody(HttpResponseMessage response)
    {
        try
        {
            return await response.Content.ReadAsStringAsync();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = AntiforgeryRegex().Match(html);
        if (!match.Success)
        {
            var snippet = html.Length > 800 ? html[..800] : html;
            throw new InvalidOperationException("No antiforgery token found on the page. Page was:\n" + snippet);
        }

        return match.Groups["v"].Value;
    }

    private static string? ExtractQueryValue(string url, string key)
    {
        var query = url.Contains('?', StringComparison.Ordinal) ? url[(url.IndexOf('?', StringComparison.Ordinal) + 1)..] : string.Empty;
        foreach (var pair in query.Split('&'))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0] == key)
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        return null;
    }

    [GeneratedRegex("""name="__RequestVerificationToken"[^>]*value="(?<v>[^"]+)""")]
    private static partial Regex AntiforgeryRegex();
}
