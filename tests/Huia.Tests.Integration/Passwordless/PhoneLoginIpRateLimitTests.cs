using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Huia.Tests.Integration.Passwordless;

/// <summary>
/// Covers the per-client-IP rate limit's enforcement specifically — that it rejects a second request even
/// for a *different* phone number, which the per-phone-number limit alone (relaxed to effectively unlimited
/// in this factory) never would. See <see cref="PhoneLoginIpRateLimitTestFactory"/>'s own doc comment for why
/// this needs its own dedicated factory/fixture rather than sharing one with
/// <see cref="PhoneLoginBotProtectionTests"/>.
/// </summary>
public class PhoneLoginIpRateLimitTests(PhoneLoginIpRateLimitTestFactory factory)
    : IClassFixture<PhoneLoginIpRateLimitTestFactory>
{
    [Fact]
    public async Task RequestOtp_SecondRequestFromSameIp_RedirectsWithCooldown_EvenForADifferentPhoneNumber()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        // The phone-number limit is relaxed to 1000 in this factory and the IP limit is 1/minute — so a
        // *second* request succeeding or failing here is governed entirely by the IP check, regardless of it
        // being a different number each time. Both requests share one TestServer connection, so both see the
        // same (or equally absent) RemoteIpAddress and land in the same partition.
        using var first = await RequestOtpAsync(client, "+14155550200");
        Assert.Contains("/identity/account/phoneloginverify", first.Headers.Location!.ToString(),
            StringComparison.OrdinalIgnoreCase);

        using var second = await RequestOtpAsync(client, "+14155550201");

        Assert.Equal(HttpStatusCode.Found, second.StatusCode);
        var location = second.Headers.Location!.ToString();
        Assert.Contains("/identity/account/login", location, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("phoneloginverify", location, StringComparison.OrdinalIgnoreCase);

        using var loginPage = await client.GetAsync(second.Headers.Location);
        var html = await loginPage.Content.ReadAsStringAsync();
        Assert.Contains("requested too many codes", html, StringComparison.Ordinal);
    }

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
}
