using System.Net;
using System.Net.Http.Json;
using Huia.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.Tests.Integration.Passwordless;

/// <summary>
/// Covers the OTP-verified self-service phone-change flow (<c>ManageInfoEndpoints</c>'s
/// <c>POST info/phone</c>/<c>POST info/phone/verify</c>/<c>DELETE info/phone</c>), driven end to end against
/// real bearer-authenticated callers via <see cref="ManagePhoneChangeTestFactory"/> (needs both
/// <c>AdminTestHelpers</c>' registration-based token minting and captured SMS codes - see that factory's own
/// doc comment for why it's separate from <see cref="TodoApiFactory"/>/<see cref="PhoneLoginTestFactory"/>).
/// </summary>
public class ManagePhoneChangeTests(ManagePhoneChangeTestFactory factory) : IClassFixture<ManagePhoneChangeTestFactory>
{
    private const string InfoPath = "/api/identity/manage/info";
    private const string PhonePath = "/api/identity/manage/info/phone";
    private const string VerifyPath = "/api/identity/manage/info/phone/verify";

    [Fact]
    public async Task RequestThenVerify_WithCorrectCode_UpdatesAndConfirmsPhoneNumber()
    {
        var (client, _, _) = await AdminTestHelpers.CreateAuthorizedClientCoreAsync(factory, roles: []);
        const string phoneNumber = "+14155552671";

        var requested = await client.PostAsJsonAsync(PhonePath, new { NewPhoneNumber = phoneNumber });
        Assert.Equal(HttpStatusCode.OK, requested.StatusCode);
        var requestResponse = await requested.Content.ReadFromJsonAsync<RequestPhoneChangeResponse>();
        Assert.Contains("*", requestResponse!.MaskedPhoneNumber, StringComparison.Ordinal);

        var code = factory.SentOtpCodes.Last(c => c.PhoneNumberE164 == phoneNumber).Code;
        var verified = await client.PostAsJsonAsync(VerifyPath, new { NewPhoneNumber = phoneNumber, Code = code });

        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        var info = await verified.Content.ReadFromJsonAsync<InfoResponse>();
        Assert.Equal(phoneNumber, info!.PhoneNumber);
        Assert.True(info.IsPhoneNumberConfirmed);
    }

    [Fact]
    public async Task Verify_WithWrongCode_ReturnsValidationProblemAndLeavesPhoneNumberUnchanged()
    {
        var (client, _, _) = await AdminTestHelpers.CreateAuthorizedClientCoreAsync(factory, roles: []);
        const string phoneNumber = "+14155552672";

        var requested = await client.PostAsJsonAsync(PhonePath, new { NewPhoneNumber = phoneNumber });
        Assert.Equal(HttpStatusCode.OK, requested.StatusCode);

        var verified = await client.PostAsJsonAsync(VerifyPath, new { NewPhoneNumber = phoneNumber, Code = "000000" });
        Assert.Equal(HttpStatusCode.BadRequest, verified.StatusCode);

        var fetched = await client.GetFromJsonAsync<InfoResponse>(InfoPath);
        Assert.Null(fetched!.PhoneNumber);
    }

    [Fact]
    public async Task Verify_WithEnoughWrongCodes_LocksOutAccount()
    {
        var (client, _, _) = await AdminTestHelpers.CreateAuthorizedClientCoreAsync(factory, roles: []);
        const string phoneNumber = "+14155552673";

        var requested = await client.PostAsJsonAsync(PhonePath, new { NewPhoneNumber = phoneNumber });
        Assert.Equal(HttpStatusCode.OK, requested.StatusCode);

        // Default IdentityOptions.Lockout.MaxFailedAccessAttempts is 5 - the sample doesn't override it.
        HttpResponseMessage lastResponse;
        do
        {
            lastResponse = await client.PostAsJsonAsync(VerifyPath, new { NewPhoneNumber = phoneNumber, Code = "000000" });
        } while (lastResponse.StatusCode == HttpStatusCode.BadRequest);

        Assert.Equal(HttpStatusCode.Locked, lastResponse.StatusCode);
    }

    [Fact]
    public async Task Request_WithInvalidPhoneNumber_ReturnsValidationProblem()
    {
        var (client, _, _) = await AdminTestHelpers.CreateAuthorizedClientCoreAsync(factory, roles: []);

        var response = await client.PostAsJsonAsync(PhonePath, new { NewPhoneNumber = "not-a-phone-number" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Remove_WithPasswordOnAccount_ClearsPhoneNumber()
    {
        var (client, _, _) = await AdminTestHelpers.CreateAuthorizedClientCoreAsync(factory, roles: []);
        const string phoneNumber = "+14155552674";

        var requested = await client.PostAsJsonAsync(PhonePath, new { NewPhoneNumber = phoneNumber });
        Assert.Equal(HttpStatusCode.OK, requested.StatusCode);
        var code = factory.SentOtpCodes.Last(c => c.PhoneNumberE164 == phoneNumber).Code;
        var verified = await client.PostAsJsonAsync(VerifyPath, new { NewPhoneNumber = phoneNumber, Code = code });
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);

        var removed = await client.DeleteAsync(PhonePath);

        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        var info = await removed.Content.ReadFromJsonAsync<InfoResponse>();
        Assert.Null(info!.PhoneNumber);
        Assert.False(info.IsPhoneNumberConfirmed);
    }

    /// <summary>A phone-only, passwordless-enabled account with no other credential can't be stranded by
    /// clearing its only sign-in method (see ManageInfoEndpoints.RemovePhoneAsync).</summary>
    [Fact]
    public async Task Remove_ForPasswordlessOnlyAccountWithNoOtherCredential_ReturnsValidationProblem()
    {
        var (client, subject) = await CreatePasswordlessOnlyAuthorizedClientAsync();

        var response = await client.DeleteAsync(PhonePath);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<HuiaUser>>();
        var user = await userManager.FindByIdAsync(subject) ?? throw new InvalidOperationException("User not found.");
        Assert.NotNull(user.PhoneNumber);
    }

    /// <summary>
    /// Drives the real passwordless phone sign-in flow (request code → verify → confirm name), the same three
    /// HTTP form posts <c>PhoneLoginTests</c> uses, to create a genuinely passwordless-only account (no
    /// password, no external logins) - <c>AdminTestHelpers.CreateAuthorizedClientCoreAsync</c>'s own flow
    /// always creates a password-having account via email/password registration, so it can't produce this
    /// shape. Once cookie-signed-in this way, <see cref="AdminTestHelpers.ExchangeSignedInClientForBearerAsync"/>
    /// mints a bearer token from it exactly like that helper does for its own registered accounts.
    /// </summary>
    private async Task<(HttpClient Client, string Subject)> CreatePasswordlessOnlyAuthorizedClientAsync()
    {
        var phoneNumber = $"+1415555{Random.Shared.Next(1000, 9999)}";
        var uiClient = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        var otpToken = await IdentityUiTestHelpers.GetAntiforgeryTokenAsync(uiClient, "/identity/account/login");
        using var otpResponse = await uiClient.PostAsync("/identity/account/phonelogin",
            new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Input.CountryCode"] = "US",
                ["Input.PhoneNumber"] = phoneNumber,
                ["__RequestVerificationToken"] = otpToken,
            }));
        Assert.Equal(HttpStatusCode.Found, otpResponse.StatusCode);

        var code = factory.SentOtpCodes.Last(c => c.PhoneNumberE164 == phoneNumber).Code;
        var verifyToken =
            await IdentityUiTestHelpers.GetAntiforgeryTokenAsync(uiClient, "/identity/account/phoneloginverify");
        using var verifyResponse = await uiClient.PostAsync("/identity/account/phoneloginverify",
            new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Input.Code"] = code,
                ["__RequestVerificationToken"] = verifyToken,
            }));
        Assert.Equal(HttpStatusCode.Found, verifyResponse.StatusCode);

        var confirmToken =
            await IdentityUiTestHelpers.GetAntiforgeryTokenAsync(uiClient, "/identity/account/phoneloginconfirmation");
        using var confirmResponse = await uiClient.PostAsync("/identity/account/phoneloginconfirmation",
            new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Input.FirstName"] = "Test",
                ["Input.LastName"] = "User",
                ["__RequestVerificationToken"] = confirmToken,
            }));
        Assert.Equal(HttpStatusCode.Found, confirmResponse.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<HuiaUser>>();
            var user = await userManager.FindByNameAsync(phoneNumber)
                       ?? throw new InvalidOperationException("Passwordless user not found.");

            var client = await AdminTestHelpers.ExchangeSignedInClientForBearerAsync(factory, uiClient);
            return (client, user.Id);
        }
    }

    private sealed record InfoResponse(string? Email, bool IsEmailConfirmed, string? PhoneNumber,
        bool IsPhoneNumberConfirmed, string? FirstName, string? LastName);

    private sealed record RequestPhoneChangeResponse(string MaskedPhoneNumber);
}
