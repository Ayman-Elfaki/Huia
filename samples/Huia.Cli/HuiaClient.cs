using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Huia.Cli;

/// <summary>Talks to a Huia identity provider: the device-authorization grant, token refresh, userinfo.</summary>
public sealed class HuiaClient(HttpClient http, string issuer, string tenant, string clientId, string clientSecret, TimeProvider clock)
{
    private string TokenEndpoint => $"{issuer}/{tenant}/connect/token";
    private string DeviceEndpoint => $"{issuer}/{tenant}/connect/device";
    private string UserInfoEndpoint => $"{issuer}/{tenant}/connect/userinfo";

    /// <summary>Runs the full device-authorization grant, printing the user code, and returns the token set.</summary>
    public async Task<CachedTokens> DeviceLoginAsync(string scope, TextWriter output, CancellationToken ct)
    {
        using var deviceResponse = await http.PostAsync(DeviceEndpoint, Form(new()
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["scope"] = scope,
        }), ct);

        var device = await ReadJsonAsync(deviceResponse, ct);
        if (!deviceResponse.IsSuccessStatusCode)
        {
            throw new HuiaCliException($"Device authorization failed: {Error(device)}");
        }

        var deviceCode = device.GetProperty("device_code").GetString()!;
        var userCode = device.GetProperty("user_code").GetString()!;
        var verificationUri = device.TryGetProperty("verification_uri_complete", out var complete)
            ? complete.GetString()!
            : device.GetProperty("verification_uri").GetString()!;
        var interval = device.TryGetProperty("interval", out var i) ? i.GetInt32() : 5;

        output.WriteLine();
        output.WriteLine($"  To sign in, open:  {verificationUri}");
        output.WriteLine($"  and enter code:    {userCode}");
        output.WriteLine();
        output.WriteLine("Waiting for approval…");

        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), ct);

            using var tokenResponse = await http.PostAsync(TokenEndpoint, Form(new()
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["device_code"] = deviceCode,
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
            }), ct);

            var token = await ReadJsonAsync(tokenResponse, ct);
            if (tokenResponse.IsSuccessStatusCode)
            {
                return ToCache(token);
            }

            switch (Error(token))
            {
                case "authorization_pending":
                    continue;
                case "slow_down":
                    interval += 5;
                    continue;
                default:
                    throw new HuiaCliException($"Device login failed: {Error(token)}");
            }
        }
    }

    /// <summary>Exchanges a refresh token for a fresh token set.</summary>
    public async Task<CachedTokens> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        using var response = await http.PostAsync(TokenEndpoint, Form(new()
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
        }), ct);

        var token = await ReadJsonAsync(response, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HuiaCliException($"Token refresh failed: {Error(token)}");
        }

        return ToCache(token);
    }

    /// <summary>Calls the userinfo endpoint with a bearer token.</summary>
    public async Task<JsonElement> UserInfoAsync(string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, UserInfoEndpoint);
        request.Headers.Authorization = new("Bearer", accessToken);
        using var response = await http.SendAsync(request, ct);
        var json = await ReadJsonAsync(response, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new HuiaCliException("Not signed in (the token was rejected). Run `huia login`.");
        }

        return json;
    }

    private CachedTokens ToCache(JsonElement token) => new()
    {
        Issuer = issuer,
        Tenant = tenant,
        AccessToken = token.GetProperty("access_token").GetString()!,
        RefreshToken = token.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null,
        ExpiresAt = clock.GetUtcNow() + TimeSpan.FromSeconds(token.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600),
    };

    private static FormUrlEncodedContent Form(Dictionary<string, string> values) => new(values);

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        try
        {
            return JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body).RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonDocument.Parse("{}").RootElement.Clone();
        }
    }

    private static string Error(JsonElement json) =>
        json.TryGetProperty("error", out var e) ? e.GetString() ?? "unknown_error" : "unknown_error";
}

/// <summary>A user-facing CLI error whose message is printed without a stack trace.</summary>
public sealed class HuiaCliException(string message) : Exception(message);
