using System.Net.Http.Headers;
using Huia.Identity;
using Huia.Sms;

namespace Huia.TodoApi.Sms;

/// <summary>
/// Sends passwordless-sign-in OTP codes over SMS via Twilio's REST API. A plain <see cref="HttpClient"/> call
/// rather than the full Twilio SDK — this is a ~15-line sample, not worth a whole vendor SDK dependency for.
/// Configured via <c>Twilio:AccountSid</c>/<c>Twilio:AuthToken</c>/<c>Twilio:FromNumber</c> (see
/// <c>Program.cs</c>, which only registers this sender when all three are present — otherwise the sample
/// falls back to Huia's default <c>NoOpSmsSender</c>, which just logs the code in Development).
/// </summary>
public sealed class TwilioSmsSender(HttpClient httpClient, IConfiguration configuration) : ISmsSender<HuiaUser>
{
    public async Task SendOtpAsync(HuiaUser user, string phoneNumberE164, string code)
    {
        var accountSid = configuration["Twilio:AccountSid"]
                          ?? throw new InvalidOperationException("Twilio:AccountSid is not configured.");
        var authToken = configuration["Twilio:AuthToken"]
                         ?? throw new InvalidOperationException("Twilio:AuthToken is not configured.");
        var fromNumber = configuration["Twilio:FromNumber"]
                          ?? throw new InvalidOperationException("Twilio:FromNumber is not configured.");

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://api.twilio.com/2010-04-01/Accounts/{accountSid}/Messages.json");

        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{accountSid}:{authToken}")));

        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["To"] = phoneNumberE164,
            ["From"] = fromNumber,
            ["Body"] = $"Your sign-in code is {code}.",
        });

        using var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }
}
