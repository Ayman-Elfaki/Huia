using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Huia.Passwordless;

/// <summary>
/// Enabled implementation of <see cref="ITurnstileVerifier"/>, registered as a typed <see cref="HttpClient"/>
/// client by <c>AddHuia</c> when <c>PasswordlessFlowOptions.UseTurnstile</c> was called. Posts the widget's
/// token to Cloudflare's <c>siteverify</c> endpoint — see
/// <see href="https://developers.cloudflare.com/turnstile/get-started/server-side-validation/"/>.
/// </summary>
internal sealed class CloudflareTurnstileVerifier(
    HttpClient httpClient,
    TurnstileOptions options,
    ILogger<CloudflareTurnstileVerifier> logger) : ITurnstileVerifier
{
    private const string SiteVerifyUrl = "https://challenges.cloudflare.com/turnstile/v0/siteverify";

    public async Task<bool> VerifyAsync(string? token, string? remoteIpAddress,
        CancellationToken cancellationToken = default)
    {
        // A missing token (the widget never ran, or client-side JS was stripped/blocked) is exactly the case
        // this feature exists to catch — never worth a round trip to Cloudflare to confirm.
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        var form = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["secret"] = options.SecretKey,
            ["response"] = token,
        };
        if (!string.IsNullOrEmpty(remoteIpAddress))
        {
            form["remoteip"] = remoteIpAddress;
        }

        using var response = await httpClient.PostAsync(SiteVerifyUrl, new FormUrlEncodedContent(form),
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Turnstile siteverify request failed with status {StatusCode}.",
                (int)response.StatusCode);
            return false;
        }

        var result = await response.Content.ReadFromJsonAsync<SiteVerifyResponse>(cancellationToken)
            .ConfigureAwait(false);
        if (result is null || !result.Success)
        {
            logger.LogInformation("Turnstile verification failed: {ErrorCodes}.",
                result?.ErrorCodes is { Length: > 0 } codes ? string.Join(", ", codes) : "(none reported)");
        }

        return result?.Success ?? false;
    }

    private sealed record SiteVerifyResponse(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("error-codes")] string[]? ErrorCodes);
}
