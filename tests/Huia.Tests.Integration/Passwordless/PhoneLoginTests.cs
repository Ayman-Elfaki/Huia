using System.Net;
using Huia.Identity;
using Huia.Stores;
using Huia.TodoApi.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.Tests.Integration.Passwordless;

/// <summary>
/// End-to-end coverage for passwordless phone sign-in: the request-code/verify-code/confirm-name round trip,
/// the returning-user shortcut, the phone-number-collision-creates-a-second-account rule, per-phone-number
/// rate limiting, the tamper-proof <c>Huia.PhoneVerification</c> cookie hand-off, lockout on repeated wrong
/// codes, antiforgery, and enumeration resistance. See docs/passwordless.md.
/// </summary>
public class PhoneLoginTests(PhoneLoginTestFactory factory) : IClassFixture<PhoneLoginTestFactory>
{
    private const string Password = "P@ssw0rd123!";
    private static int _phoneCounter;

    [Fact]
    public async Task NewNumber_CompletesConfirmation_CreatesPasswordlessAccountAndSignsIn()
    {
        var phoneNumber = NewPhoneNumber();
        using var client = CreateClient();

        using var otpResponse = await RequestOtpAsync(client, phoneNumber);
        Assert.Equal(HttpStatusCode.Found, otpResponse.StatusCode);
        Assert.Contains("/identity/account/phoneloginverify",
            otpResponse.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);

        var code = LastCodeFor(phoneNumber);
        using var verifyResponse = await VerifyOtpAsync(client, code);
        Assert.Equal(HttpStatusCode.Found, verifyResponse.StatusCode);
        Assert.Contains("/identity/account/phoneloginconfirmation",
            verifyResponse.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);

        using var confirmResponse = await CompleteConfirmationAsync(client, "Test", "User");
        Assert.Equal(HttpStatusCode.Found, confirmResponse.StatusCode);

        var user = await FindUserByPhoneAsync(phoneNumber);
        Assert.NotNull(user);
        Assert.True(user.PasswordlessLoginEnabled);
        Assert.True(user.PhoneNumberConfirmed);
        Assert.Equal("Test", user.FirstName);
        Assert.Equal("User", user.LastName);
    }

    [Fact]
    public async Task ExistingOptedInNumber_SignsInDirectly_NoConfirmationNoDuplicateAccount()
    {
        var phoneNumber = NewPhoneNumber();

        using (var firstClient = CreateClient())
        {
            await RequestOtpAsync(firstClient, phoneNumber);
            var code = LastCodeFor(phoneNumber);
            await VerifyOtpAsync(firstClient, code);
            using var confirm = await CompleteConfirmationAsync(firstClient, "Test", "User");
            Assert.Equal(HttpStatusCode.Found, confirm.StatusCode);
        }

        using var secondClient = CreateClient();
        await RequestOtpAsync(secondClient, phoneNumber);
        var secondCode = LastCodeFor(phoneNumber);
        using var secondVerify = await VerifyOtpAsync(secondClient, secondCode);

        // Signed straight in this time — no confirmation redirect, no second account.
        Assert.Equal(HttpStatusCode.Found, secondVerify.StatusCode);
        Assert.DoesNotContain("phoneloginconfirmation", secondVerify.Headers.Location!.ToString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await CountUsersByPhoneAsync(phoneNumber));
    }

    /// <summary>
    /// With the rate limit effectively unlimited (this factory's own override), Resend reaches
    /// <c>IPhoneOtpRateLimiter</c>'s permit-granting branch every time — the opaque, silently-swallowed path
    /// is covered instead by <c>PhoneOtpRateLimitTests</c>, which keeps the tight production default.
    /// </summary>
    [Fact]
    public async Task Resend_SendsAFreshCode_ThatVerifiesSuccessfully()
    {
        var phoneNumber = NewPhoneNumber();
        using var client = CreateClient();

        await RequestOtpAsync(client, phoneNumber);

        using var resendResponse = await ResendOtpAsync(client);
        Assert.Equal(HttpStatusCode.Found, resendResponse.StatusCode);
        Assert.Contains("/identity/account/phoneloginverify",
            resendResponse.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);

        var codesForNumber = factory.SentOtpCodes
            .Where(c => string.Equals(c.PhoneNumberE164, phoneNumber, StringComparison.Ordinal))
            .ToList();
        Assert.Equal(2, codesForNumber.Count);
        var resentCode = codesForNumber[^1].Code;

        using var verifyResponse = await VerifyOtpAsync(client, resentCode);
        Assert.Equal(HttpStatusCode.Found, verifyResponse.StatusCode);
        Assert.Contains("/identity/account/phoneloginconfirmation",
            verifyResponse.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PhoneNumberCollidesWithNonPasswordlessAccount_CreatesSeparateAccount()
    {
        var phoneNumber = NewPhoneNumber();
        var email = NewEmail();

        using var setupClient = CreateClient();
        await IdentityUiTestHelpers.RegisterAsync(setupClient, email, Password);

        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<HuiaUser>>();
            var existing = await userManager.FindByEmailAsync(email);
            existing!.PhoneNumber = phoneNumber;
            existing.NormalizedPhoneNumber = phoneNumber;
            await userManager.UpdateAsync(existing);
        }

        using var client = CreateClient();
        await RequestOtpAsync(client, phoneNumber);
        var code = LastCodeFor(phoneNumber);
        await VerifyOtpAsync(client, code);
        using var confirm = await CompleteConfirmationAsync(client, "New", "Passwordless");

        Assert.Equal(HttpStatusCode.Found, confirm.StatusCode);
        Assert.Equal(2, await CountUsersByPhoneAsync(phoneNumber));
    }

    [Fact]
    public async Task Verify_WithoutPendingCookie_RedirectsBackToPhoneLoginWithoutLeakingState()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync("/identity/account/phoneloginverify");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        Assert.Contains("/identity/account/phonelogin", location, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("phoneloginverify", location, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Confirmation_WithoutVerifiedCookie_RedirectsBackToPhoneLogin()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync("/identity/account/phoneloginconfirmation");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Contains("/identity/account/phonelogin", response.Headers.Location!.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Confirmation_ReachedWithOnlyAPendingUnverifiedCookie_RedirectsBackToPhoneLogin()
    {
        // Requesting an OTP (but never verifying it) issues the PhoneVerification cookie without the
        // "verified" claim — proves the confirmation page can't be skipped to off the back of that alone.
        var phoneNumber = NewPhoneNumber();
        using var client = CreateClient();
        await RequestOtpAsync(client, phoneNumber);

        using var response = await client.GetAsync("/identity/account/phoneloginconfirmation");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        Assert.Contains("/identity/account/phonelogin", location, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("phoneloginconfirmation", location, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Verify_RepeatedWrongCode_LocksOut()
    {
        var phoneNumber = NewPhoneNumber();
        using var client = CreateClient();
        await RequestOtpAsync(client, phoneNumber);

        HttpResponseMessage? lastResponse = null;
        try
        {
            // ASP.NET Core Identity's default MaxFailedAccessAttempts is 5 — the 5th wrong code already trips
            // the lockout (mirrors IdentityUiSecurityTests.Login_Post_AfterFiveFailedAttempts_LocksOutEvenWithCorrectPassword).
            for (var attempt = 0; attempt < 5; attempt++)
            {
                lastResponse?.Dispose();
                lastResponse = await VerifyOtpAsync(client, "000000");
            }

            Assert.Equal(HttpStatusCode.Found, lastResponse!.StatusCode);
            Assert.Equal("/identity/account/lockout", lastResponse.Headers.Location?.OriginalString);
        }
        finally
        {
            lastResponse?.Dispose();
        }
    }

    [Fact]
    public async Task PhoneLogin_Post_WithoutAntiforgeryToken_IsRejected()
    {
        using var client = CreateClient();
        await client.GetAsync("/identity/account/login");

        using var response = await client.PostAsync("/identity/account/phonelogin",
            new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Input.PhoneNumber"] = NewPhoneNumber(),
            }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PhoneLoginVerify_Post_WithoutAntiforgeryToken_IsRejected()
    {
        var phoneNumber = NewPhoneNumber();
        using var client = CreateClient();
        await RequestOtpAsync(client, phoneNumber);
        await client.GetAsync("/identity/account/phoneloginverify");

        using var response = await client.PostAsync("/identity/account/phoneloginverify",
            new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Input.Code"] = "000000",
            }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task NeverSeenNumber_And_KnownOptedInNumber_ProduceIdenticalRedirectShape()
    {
        var knownNumber = NewPhoneNumber();
        using (var setupClient = CreateClient())
        {
            await RequestOtpAsync(setupClient, knownNumber);
            var code = LastCodeFor(knownNumber);
            await VerifyOtpAsync(setupClient, code);
            await CompleteConfirmationAsync(setupClient, "Known", "User");
        }

        var unseenNumber = NewPhoneNumber();

        using var unseenClient = CreateClient();
        using var unseenResponse = await RequestOtpAsync(unseenClient, unseenNumber);

        using var knownClient = CreateClient();
        using var knownResponse = await RequestOtpAsync(knownClient, knownNumber);

        Assert.Equal(unseenResponse.StatusCode, knownResponse.StatusCode);
        // Path only (both redirects carry no query string here, but comparing the whole OriginalString would
        // break if that ever changed) — Location is a relative URI (RedirectToPage), so AbsolutePath isn't
        // usable directly.
        var unseenPath = unseenResponse.Headers.Location!.OriginalString.Split('?')[0];
        var knownPath = knownResponse.Headers.Location!.OriginalString.Split('?')[0];
        Assert.Equal(unseenPath, knownPath);
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

    private static async Task<HttpResponseMessage> CompleteConfirmationAsync(HttpClient client, string firstName, string lastName)
    {
        var token = await IdentityUiTestHelpers.GetAntiforgeryTokenAsync(client, "/identity/account/phoneloginconfirmation");
        return await client.PostAsync("/identity/account/phoneloginconfirmation",
            new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Input.FirstName"] = firstName,
                ["Input.LastName"] = lastName,
                ["__RequestVerificationToken"] = token,
            }));
    }

    private string LastCodeFor(string phoneNumber) =>
        factory.SentOtpCodes.Last(c => string.Equals(c.PhoneNumberE164, phoneNumber, StringComparison.Ordinal)).Code;

    private async Task<HuiaUser?> FindUserByPhoneAsync(string phoneNumber)
    {
        using var scope = factory.Services.CreateScope();
        var phoneStore = scope.ServiceProvider.GetRequiredService<IHuiaPhoneNumberStore>();
        return await phoneStore.FindByNormalizedPhoneNumberAsync(phoneNumber, CancellationToken.None);
    }

    private async Task<int> CountUsersByPhoneAsync(string phoneNumber)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HuiaAppDbContext>();
        return await db.Users.CountAsync(u => u.NormalizedPhoneNumber == phoneNumber);
    }

    // A valid, distinct-per-call NANP number (san-francisco/415 area code, 555 exchange — libphonenumber
    // treats the vast majority of the 555 exchange as a genuinely valid, assignable number; only 555-0100
    // through 555-0199 are reserved fictional numbers, so starting the suffix at 0200 avoids that block).
    private static string NewPhoneNumber()
    {
        var n = Interlocked.Increment(ref _phoneCounter);
        var suffix = 200 + n % 9700;
        return $"+1415555{suffix:D4}";
    }

    private static string NewEmail() => $"phone-collision-test-{Guid.NewGuid():N}@example.com";
}
