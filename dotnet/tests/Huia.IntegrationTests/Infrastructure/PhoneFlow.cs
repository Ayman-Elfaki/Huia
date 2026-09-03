using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.IdentityModel.Tokens;

namespace Huia.IntegrationTests.Infrastructure;

/// <summary>Drives the passwordless SMS pages (phone number → code → sign-in / complete-profile).</summary>
public sealed partial class PhoneFlow(HuiaTestHost host, string tenant)
{
    private readonly HttpClient _client = host.CreateClient();

    /// <summary>The URL the last hop redirected to (an absolute-or-local location).</summary>
    public string? LastLocation { get; private set; }

    /// <summary>Requests a code for a number. Returns the <c>flow</c> token from the redirect to VerifyOtp.</summary>
    /// <param name="phoneNumber">The number to enter.</param>
    /// <param name="country">An optional ISO country for a national number.</param>
    /// <returns>The flow token.</returns>
    public async Task<string> RequestCodeAsync(string phoneNumber, string? country = null)
    {
        var response = await RequestCodeRawAsync(phoneNumber, country);
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect, await Body(response));
        LastLocation = response.Headers.Location!.ToString();
        return ExtractQuery(LastLocation, "flow")!;
    }

    /// <summary>
    /// Posts the phone tab of the login page and returns the raw response — a 302 to VerifyOtp on
    /// success, or a 200 re-render when the request is rejected (bad number, throttled, ...).
    /// </summary>
    /// <param name="phoneNumber">The number to enter.</param>
    /// <param name="country">An optional ISO country for a national number.</param>
    /// <returns>The response.</returns>
    public async Task<HttpResponseMessage> RequestCodeRawAsync(string phoneNumber, string? country = null)
    {
        var loginUrl = $"/{tenant}/identity/account/login";
        var token = await GetAntiforgeryAsync(loginUrl);

        var response = await _client.PostAsync($"{loginUrl}?handler=Phone", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.PhoneNumber"] = phoneNumber,
            ["Input.Country"] = country ?? string.Empty,
            ["__RequestVerificationToken"] = token,
        }));

        LastLocation = response.Headers.Location?.ToString();
        return response;
    }

    /// <summary>Submits a code for a flow. Returns the raw HTTP response (302 on success).</summary>
    /// <param name="flow">The flow token.</param>
    /// <param name="code">The one-time code.</param>
    /// <returns>The response.</returns>
    public async Task<HttpResponseMessage> SubmitCodeAsync(string flow, string code)
    {
        var url = $"/{tenant}/identity/account/verifyotp?flow={Uri.EscapeDataString(flow)}";
        var token = await GetAntiforgeryAsync(url);

        var response = await _client.PostAsync($"/{tenant}/identity/account/verifyotp", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Flow"] = flow,
            ["Input.Code"] = code,
            ["__RequestVerificationToken"] = token,
        }));

        LastLocation = response.Headers.Location?.ToString();
        return response;
    }

    /// <summary>Submits the complete-profile form for a flow.</summary>
    /// <param name="flow">The flow token (from a VerifyOtp redirect to CompleteProfile).</param>
    /// <param name="firstName">Given name.</param>
    /// <param name="lastName">Family name.</param>
    /// <returns>The response (302 on success).</returns>
    public async Task<HttpResponseMessage> CompleteProfileAsync(string flow, string firstName, string lastName)
    {
        var url = $"/{tenant}/identity/account/completeprofile?flow={Uri.EscapeDataString(flow)}";
        var token = await GetAntiforgeryAsync(url);

        var response = await _client.PostAsync($"/{tenant}/identity/account/completeprofile", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Flow"] = flow,
            ["Input.FirstName"] = firstName,
            ["Input.LastName"] = lastName,
            ["__RequestVerificationToken"] = token,
        }));

        LastLocation = response.Headers.Location?.ToString();
        return response;
    }

    /// <summary>
    /// Completes an authorization-code + PKCE exchange on the already-signed-in cookie session and
    /// returns the access token. Call after a successful <see cref="SubmitCodeAsync"/>.
    /// </summary>
    /// <param name="clientId">A seeded interactive client id for this tenant.</param>
    /// <param name="redirectUri">That client's redirect URI.</param>
    /// <param name="scope">The requested scope.</param>
    /// <returns>The access token.</returns>
    public async Task<string> GetAccessTokenAsync(string clientId, string redirectUri, string scope = "openid profile email")
    {
        var verifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var authorizeUrl =
            $"/{tenant}/connect/authorize?response_type=code&client_id={Uri.EscapeDataString(clientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}&scope={Uri.EscapeDataString(scope)}" +
            $"&code_challenge={challenge}&code_challenge_method=S256&state={Guid.NewGuid():N}";

        var toCallback = await _client.GetAsync(authorizeUrl);
        toCallback.StatusCode.ShouldBe(HttpStatusCode.Redirect, await Body(toCallback));
        var callback = toCallback.Headers.Location!.ToString();
        if (callback.Contains("/identity/account/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The phone flow is not signed in with a complete profile: " + callback);
        }

        var code = ExtractQuery(callback, "code") ?? throw new InvalidOperationException("No code on " + callback);

        var tokenResponse = await _client.PostAsync($"/{tenant}/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = clientId,
            ["code_verifier"] = verifier,
        }));

        var body = await Body(tokenResponse);
        tokenResponse.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("access_token").GetString()!;
    }

    /// <summary>Whether the session cookie is now set (i.e. the flow signed the user in).</summary>
    public async Task<bool> IsSignedInAsync()
    {
        var probe = await _client.GetAsync($"/{tenant}/connect/authorize?response_type=code&client_id=x&redirect_uri=https://x&scope=openid&code_challenge=y&code_challenge_method=S256&state=z");
        // Signed-in: OpenIddict rejects the bogus client (400/302 with error). Signed-out: redirect to login.
        var location = probe.Headers.Location?.ToString() ?? string.Empty;
        return !location.Contains("/identity/account/login", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> GetAntiforgeryAsync(string url)
    {
        var html = await _client.GetStringAsync(url);
        var match = AntiforgeryRegex().Match(html);
        if (!match.Success)
        {
            throw new InvalidOperationException("No antiforgery token on " + url + "\n" + (html.Length > 600 ? html[..600] : html));
        }

        return match.Groups["v"].Value;
    }

    /// <summary>Pulls the opaque <c>flow</c> token out of a redirect URL to VerifyOtp / CompleteProfile.</summary>
    /// <param name="url">The redirect location.</param>
    /// <returns>The flow token.</returns>
    public static string FlowTokenOf(string url) =>
        ExtractQuery(url, "flow") ?? throw new InvalidOperationException("No flow token in " + url);

    private static string? ExtractQuery(string url, string key)
    {
        var q = url.Contains('?', StringComparison.Ordinal) ? url[(url.IndexOf('?', StringComparison.Ordinal) + 1)..] : string.Empty;
        foreach (var pair in q.Split('&'))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0] == key)
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        return null;
    }

    private static async Task<string> Body(HttpResponseMessage r)
    {
        try
        {
            return await r.Content.ReadAsStringAsync();
        }
        catch
        {
            return string.Empty;
        }
    }

    [GeneratedRegex("""name="__RequestVerificationToken"[^>]*value="(?<v>[^"]+)""")]
    private static partial Regex AntiforgeryRegex();
}
