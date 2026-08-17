using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Huia.Tests.Integration.Passwordless;

/// <summary>
/// Covers the default per-phone-number OTP rate limit (1/minute) actually being enforced end to end — kept
/// separate from <see cref="PhoneLoginTests"/> (which relaxes the limit so its other assertions aren't
/// affected by it) via its own dedicated <see cref="PhoneLoginRateLimitTestFactory"/>.
/// </summary>
public class PhoneOtpRateLimitTests(PhoneLoginRateLimitTestFactory factory) : IClassFixture<PhoneLoginRateLimitTestFactory>
{
    private static int _phoneCounter;

    [Fact]
    public async Task SecondRequestWithinAMinute_RedirectsToLoginWithCooldownMessage_ReturnUrlPreserved_NoSecondCodeSent()
    {
        var phoneNumber = NewPhoneNumber();
        using var client = CreateClient();

        using var first = await RequestOtpAsync(client, phoneNumber);
        Assert.Contains("/identity/account/phoneloginverify",
            first.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);

        using var second = await RequestOtpAsync(client, phoneNumber, returnUrl: "/some/deep/link");

        // A denied request is no longer opaque: it goes back to Login (not PhoneLoginVerify — there's no
        // pending code to verify), with the cooldown surfaced and returnUrl carried through so it isn't lost.
        Assert.Equal(HttpStatusCode.Found, second.StatusCode);
        var location = second.Headers.Location!.ToString();
        Assert.Contains("/identity/account/login", location, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Uri.EscapeDataString("/some/deep/link"), location, StringComparison.Ordinal);

        using var loginPage = await client.GetAsync(second.Headers.Location);
        var html = await loginPage.Content.ReadAsStringAsync();
        Assert.Contains("requested too many codes", html, StringComparison.Ordinal);

        // Only the first request's code was ever actually sent.
        Assert.Single(factory.SentOtpCodes, c => string.Equals(c.PhoneNumberE164, phoneNumber, StringComparison.Ordinal));
    }

    /// <summary>
    /// PhoneLoginVerify's Resend button posts back to the same phone number the pending cookie already
    /// carries, going through the exact same <c>IPhoneOtpRateLimiter</c> as the original send — so clicking
    /// it inside that same one-minute window collides with the cap the initial send already consumed,
    /// exactly like a second <c>PhoneLogin</c> submission would (see the sibling test above). Unlike that
    /// initial send, a denied resend shows the real cooldown rather than staying opaque — see
    /// <c>PhoneLoginVerifyModel.OnPostResendAsync</c>'s own doc comment for why that's safe here — and
    /// leaves the pending cookie (and the code already sent for it) untouched, so it still verifies.
    /// </summary>
    [Fact]
    public async Task Resend_WithinTheSameMinuteAsTheOriginalSend_ShowsCooldown_OriginalCodeStillValid()
    {
        var phoneNumber = NewPhoneNumber();
        using var client = CreateClient();

        await RequestOtpAsync(client, phoneNumber);
        var originalCode = factory.SentOtpCodes.Last(c =>
            string.Equals(c.PhoneNumberE164, phoneNumber, StringComparison.Ordinal)).Code;

        using var resendResponse = await ResendOtpAsync(client);

        // Denied resends re-render the verify page itself (still holding the pending cookie), not a redirect.
        Assert.Equal(HttpStatusCode.OK, resendResponse.StatusCode);
        var html = await resendResponse.Content.ReadAsStringAsync();
        Assert.Contains("requested too many codes", html, StringComparison.Ordinal);

        // No second code for this number — the resend never actually reached IPhoneOtpRateLimiter's
        // permit-granting branch.
        Assert.Single(factory.SentOtpCodes, c => string.Equals(c.PhoneNumberE164, phoneNumber, StringComparison.Ordinal));

        using var verifyResponse = await VerifyOtpAsync(client, originalCode);
        Assert.Equal(HttpStatusCode.Found, verifyResponse.StatusCode);
        Assert.DoesNotContain("phoneloginverify", verifyResponse.Headers.Location!.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RequestsForDifferentPhoneNumbers_AreNotRateLimitedTogether()
    {
        var firstNumber = NewPhoneNumber();
        var secondNumber = NewPhoneNumber();

        using var client = CreateClient();
        await RequestOtpAsync(client, firstNumber);
        await RequestOtpAsync(client, firstNumber); // exhausts firstNumber's limit

        using var secondNumberResponse = await RequestOtpAsync(client, secondNumber);

        Assert.Equal(HttpStatusCode.Found, secondNumberResponse.StatusCode);
        Assert.Single(factory.SentOtpCodes, c => string.Equals(c.PhoneNumberE164, secondNumber, StringComparison.Ordinal));
    }

    private HttpClient CreateClient() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost"),
    });

    private static async Task<HttpResponseMessage> RequestOtpAsync(HttpClient client, string phoneNumber,
        string? returnUrl = null)
    {
        var token = await IdentityUiTestHelpers.GetAntiforgeryTokenAsync(client, "/identity/account/login");
        var url = returnUrl is null
            ? "/identity/account/phonelogin"
            : $"/identity/account/phonelogin?returnUrl={Uri.EscapeDataString(returnUrl)}";
        return await client.PostAsync(url,
            new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Input.CountryCode"] = "US",
                ["Input.PhoneNumber"] = phoneNumber,
                ["__RequestVerificationToken"] = token,
            }));
    }

    private static async Task<HttpResponseMessage> ResendOtpAsync(HttpClient client)
    {
        var token = await IdentityUiTestHelpers.GetAntiforgeryTokenAsync(client, "/identity/account/phoneloginverify");
        return await client.PostAsync("/identity/account/phoneloginverify?handler=resend",
            new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["__RequestVerificationToken"] = token,
            }));
    }

    private static async Task<HttpResponseMessage> VerifyOtpAsync(HttpClient client, string code)
    {
        var token = await IdentityUiTestHelpers.GetAntiforgeryTokenAsync(client, "/identity/account/phoneloginverify");
        return await client.PostAsync("/identity/account/phoneloginverify",
            new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Input.Code"] = code,
                ["__RequestVerificationToken"] = token,
            }));
    }

    private static string NewPhoneNumber()
    {
        var n = Interlocked.Increment(ref _phoneCounter);
        var suffix = 200 + n % 9700;
        return $"+1415555{suffix:D4}";
    }
}
