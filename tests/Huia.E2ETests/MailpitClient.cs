using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Huia.E2ETests;

/// <summary>Minimal client over Mailpit's REST API (<c>/api/v1</c>).</summary>
public sealed partial class MailpitClient(string baseUrl)
{
    private readonly HttpClient _http = new() { BaseAddress = new Uri(baseUrl) };

    /// <summary>Blocks until the Mailpit HTTP API answers.</summary>
    public async Task WaitUntilReadyAsync()
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            try
            {
                using var response = await _http.GetAsync("/api/v1/info");
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // not up yet
            }

            await Task.Delay(500);
        }

        throw new InvalidOperationException("Mailpit REST API never became ready.");
    }

    /// <summary>Deletes every captured message.</summary>
    public Task ClearAsync() => _http.DeleteAsync("/api/v1/messages");

    /// <summary>
    /// Polls until a message addressed to <paramref name="toAddress"/> arrives, then returns the first
    /// <c>/identity/account/confirmemail</c> or <c>/identity/account/resetpassword</c> URL found in its
    /// text (or HTML) body.
    /// </summary>
    public async Task<string> WaitForActionUrlAsync(string toAddress, CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var summary = await _http.GetFromJsonAsync<JsonElement>(
                $"/api/v1/search?query={Uri.EscapeDataString($"to:{toAddress}")}", cancellationToken);

            if (summary.TryGetProperty("messages", out var messages) && messages.GetArrayLength() > 0)
            {
                var id = messages[0].GetProperty("ID").GetString();
                var message = await _http.GetFromJsonAsync<JsonElement>($"/api/v1/message/{id}", cancellationToken);

                var body = Text(message, "Text") ?? Text(message, "HTML") ?? "";
                var match = ActionUrlRegex().Match(body);
                if (match.Success)
                {
                    return match.Value.TrimEnd('.', ')', '"', '\'', '>');
                }
            }

            await Task.Delay(500, cancellationToken);
        }

        throw new InvalidOperationException($"No actionable Mailpit message arrived for {toAddress}.");
    }

    private static string? Text(JsonElement message, string property) =>
        message.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    [GeneratedRegex(@"https?://[^\s""'<>]+/identity/account/(?:confirmemail|resetpassword)[^\s""'<>]*")]
    private static partial Regex ActionUrlRegex();
}
