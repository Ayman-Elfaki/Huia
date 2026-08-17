using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Huia.Tests.Integration.Passwordless;

/// <summary>
/// Covers the two new, configurable layers <c>PhoneLoginModel.OnPostAsync</c> checks alongside the
/// per-phone-number rate limit: the per-client-IP rate limit (<c>PasswordlessFlowOptions.EnableIpRateLimiting</c>)
/// and the Cloudflare Turnstile bot check (<c>PasswordlessFlowOptions.UseTurnstile</c>). Both are wired to
/// generic no-op implementations by default (see <c>NoOpPhoneIpRateLimiter</c>/<c>NoOpTurnstileVerifier</c>),
/// which <see cref="PhoneOtpRateLimitTests"/>/<see cref="PhoneLoginTests"/> already exercise implicitly every
/// time they send a request that succeeds — this class is the one that actually turns both on and proves they
/// reject.
/// </summary>
public class PhoneLoginBotProtectionTests(PhoneLoginBotProtectionTestFactory factory)
    : IClassFixture<PhoneLoginBotProtectionTestFactory>
{
    [Fact]
    public async Task RequestOtp_TurnstileFails_RedirectsToLoginWithBotError_NoOtpSent()
    {
        factory.TurnstileVerifier.ShouldVerify = false;
        var phoneNumber = NewPhoneNumber();
        using var client = CreateClient();

        using var response = await RequestOtpAsync(client, phoneNumber);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        Assert.Contains("/identity/account/login", location, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("phoneloginverify", location, StringComparison.OrdinalIgnoreCase);

        using var loginPage = await client.GetAsync(response.Headers.Location);
        var html = await loginPage.Content.ReadAsStringAsync();
        // "not a robot" rather than the full message: the apostrophes on either side of it ("couldn't",
        // "you're") get HTML-entity-encoded in the rendered page, which a literal substring match wouldn't survive.
        Assert.Contains("not a robot", html, StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(factory.SentOtpCodes, c => string.Equals(c.PhoneNumberE164, phoneNumber, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RequestOtp_TurnstilePasses_SendsOtpAndRedirectsToVerify()
    {
        factory.TurnstileVerifier.ShouldVerify = true;
        var phoneNumber = NewPhoneNumber();
        using var client = CreateClient();

        using var response = await RequestOtpAsync(client, phoneNumber);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Contains("/identity/account/phoneloginverify", response.Headers.Location!.ToString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(factory.SentOtpCodes, c => string.Equals(c.PhoneNumberE164, phoneNumber, StringComparison.Ordinal));
    }

    private HttpClient CreateClient() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost"),
    });

    private static async Task<HttpResponseMessage> RequestOtpAsync(HttpClient client, string phoneNumber)
    {
        var token = await IdentityUiTestHelpers.GetAntiforgeryTokenAsync(client, "/identity/account/login");
        return await client.PostAsync("/identity/account/phonelogin",
            new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Input.CountryCode"] = "US",
                ["Input.PhoneNumber"] = phoneNumber,
                ["__RequestVerificationToken"] = token,
            }));
    }

    // A valid, distinct-per-call NANP number (san-francisco/415 area code, 555 exchange) — see
    // PhoneLoginTests.NewPhoneNumber's own comment on why the suffix starts at 0200.
    private static int _phoneCounter;

    private static string NewPhoneNumber()
    {
        var n = Interlocked.Increment(ref _phoneCounter);
        var suffix = 200 + n % 9700;
        return $"+1415555{suffix:D4}";
    }
}
