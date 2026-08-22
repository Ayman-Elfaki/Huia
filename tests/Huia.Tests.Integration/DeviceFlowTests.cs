using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Huia.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.Tests.Integration;


/// <summary>
/// End-to-end coverage for the device authorization flow (RFC 8628): <c>/connect/device</c>,
/// <c>/connect/verify</c> (both the JSON API and the bundled <c>Device</c> Razor Page — see
/// <c>DeviceVerificationService</c>'s doc comment for how the two share their approval logic), and the
/// device_code grant at <c>/connect/token</c>. The sample registers a "huia-cli" device application
/// specifically for this (see <c>Huia.TodoApi/Program.cs</c>).
/// </summary>
/// <remarks>
/// This flow had no test coverage before it was added, which let three real bugs ship unnoticed: the
/// verification POST bound a JSON body while OpenIddict's own end-user-verification endpoint only accepts
/// <c>application/x-www-form-urlencoded</c>; the resulting antiforgery requirement was never wired into the
/// sample; and the approval step never attached scope resources, so a device-flow token never carried an
/// <c>aud</c> claim regardless of which scopes were granted — silently failing any resource server's
/// <c>RequireAudiences</c> check. <see cref="DeviceCodeGrant_ApprovedAsAdminViaJsonApi_IssuesUsableToken"/> and
/// <see cref="DeviceCodeGrant_ApprovedThroughRealDevicePage_IssuesUsableToken"/> are the regression tests for
/// that last one specifically: <c>Huia.TodoApi</c> gates every endpoint on the <c>todo-api</c> audience
/// (<c>RequireAudiences</c>) on top of gating admin endpoints on the "Admin" role, so a token missing either
/// would fail the final call.
/// </remarks>
public class DeviceFlowTests(TodoApiFactory factory) : IClassFixture<TodoApiFactory>
{
    private const string Password = "P@ssw0rd123!";
    private const string DeviceClientId = "huia-cli";

    [Fact]
    public async Task DeviceCodeGrant_ApprovedAsAdminViaJsonApi_IssuesUsableToken()
    {
        var email = $"device-json-{Guid.NewGuid():N}@example.com";
        using var signedInClient = factory.CreateClient();
        await IdentityUiTestHelpers.RegisterAsync(signedInClient, email, Password);
        await PromoteToAdminAsync(email);

        using var deviceClient = factory.CreateClient();
        var (deviceCode, userCode, _) = await RequestDeviceCodeAsync(deviceClient, "roles todos");

        await ApproveOrDenyViaJsonApiAsync(signedInClient, userCode, accept: true);

        var (accessToken, error) = await PollTokenOnceAsync(deviceClient, deviceCode);
        Assert.Null(error);
        Assert.False(string.IsNullOrEmpty(accessToken));

        await AssertTokenCanCallAdminApiAsync(accessToken!);
    }

    [Fact]
    public async Task DeviceCodeGrant_ApprovedThroughRealDevicePage_IssuesUsableToken()
    {
        var email = $"device-page-{Guid.NewGuid():N}@example.com";
        using var signedInClient = factory.CreateClient();
        await IdentityUiTestHelpers.RegisterAsync(signedInClient, email, Password);
        await PromoteToAdminAsync(email);

        using var deviceClient = factory.CreateClient();
        var (deviceCode, userCode, verificationUri) = await RequestDeviceCodeAsync(deviceClient, "roles todos");
        Assert.Contains("/identity/account/device", verificationUri, StringComparison.Ordinal);

        var pageUrl = $"/identity/account/device?user_code={userCode}";
        var antiforgeryToken = await IdentityUiTestHelpers.GetAntiforgeryTokenAsync(signedInClient, pageUrl);

        using var approveResponse = await signedInClient.PostAsync(pageUrl, new FormUrlEncodedContent(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["user_code"] = userCode,
                ["accept"] = "true",
                ["__RequestVerificationToken"] = antiforgeryToken,
            }));
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);
        Assert.Contains("Signed in", await approveResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var (accessToken, error) = await PollTokenOnceAsync(deviceClient, deviceCode);
        Assert.Null(error);

        await AssertTokenCanCallAdminApiAsync(accessToken!);
    }

    [Fact]
    public async Task DeviceCodeGrant_DeniedViaJsonApi_TokenPollReturnsAccessDenied()
    {
        var email = $"device-deny-{Guid.NewGuid():N}@example.com";
        using var signedInClient = factory.CreateClient();
        await IdentityUiTestHelpers.RegisterAsync(signedInClient, email, Password);

        using var deviceClient = factory.CreateClient();
        var (deviceCode, userCode, _) = await RequestDeviceCodeAsync(deviceClient, "roles todos");

        await ApproveOrDenyViaJsonApiAsync(signedInClient, userCode, accept: false);

        var (accessToken, error) = await PollTokenOnceAsync(deviceClient, deviceCode);
        Assert.Null(accessToken);
        Assert.Equal("access_denied", error);
    }

    [Fact]
    public async Task DeviceCodeGrant_DeniedThroughRealDevicePage_TokenPollReturnsAccessDenied()
    {
        var email = $"device-deny-page-{Guid.NewGuid():N}@example.com";
        using var signedInClient = factory.CreateClient();
        await IdentityUiTestHelpers.RegisterAsync(signedInClient, email, Password);

        using var deviceClient = factory.CreateClient();
        var (deviceCode, userCode, _) = await RequestDeviceCodeAsync(deviceClient, "roles todos");

        var pageUrl = $"/identity/account/device?user_code={userCode}";
        var antiforgeryToken = await IdentityUiTestHelpers.GetAntiforgeryTokenAsync(signedInClient, pageUrl);

        using var denyResponse = await signedInClient.PostAsync(pageUrl, new FormUrlEncodedContent(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["user_code"] = userCode,
                ["accept"] = "false",
                ["__RequestVerificationToken"] = antiforgeryToken,
            }));
        var denyBody = await denyResponse.Content.ReadAsStringAsync();
        Assert.True(denyResponse.StatusCode == HttpStatusCode.OK, $"[{denyResponse.StatusCode}] {denyBody}");
        Assert.Contains("denied", denyBody, StringComparison.OrdinalIgnoreCase);

        var (accessToken, error) = await PollTokenOnceAsync(deviceClient, deviceCode);
        Assert.Null(accessToken);
        Assert.Equal("access_denied", error);
    }

    [Fact]
    public async Task VerifyEndpoint_InvalidUserCode_ReturnsBadRequest()
    {
        var email = $"device-invalid-{Guid.NewGuid():N}@example.com";
        using var signedInClient = factory.CreateClient();
        await IdentityUiTestHelpers.RegisterAsync(signedInClient, email, Password);

        using var response = await signedInClient.GetAsync("/connect/verify?user_code=0000-0000-0000");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DevicePage_UnauthenticatedGet_RedirectsToLogin()
    {
        using var deviceClient = factory.CreateClient();
        var (_, userCode, _) = await RequestDeviceCodeAsync(deviceClient, "roles todos");

        using var anonymousClient = factory.CreateClient();
        using var response = await anonymousClient.GetAsync($"/identity/account/device?user_code={userCode}");

        // AllowAutoRedirect (the client's default) follows the page's own RedirectToPage("./Login", ...) —
        // landing on the login page (not a bare 401) is the point: a real user clicking this link from their
        // phone needs a normal sign-in prompt, not an API-style 401.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Sign in", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private async Task PromoteToAdminAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<HuiaRoleManager>();
        if (!await roleManager.RoleExistsAsync("Admin"))
        {
            await roleManager.CreateAsync(new HuiaRole { Name = "Admin" });
        }

        var userManager = scope.ServiceProvider.GetRequiredService<HuiaUserManager>();
        var user = await userManager.FindByEmailAsync(email)
            ?? throw new InvalidOperationException("Registered user not found.");
        await userManager.AddToRoleAsync(user, "Admin");
    }

    private async Task AssertTokenCanCallAdminApiAsync(string accessToken)
    {
        using var apiClient = factory.CreateClient();
        apiClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await apiClient.GetAsync("/api/identity/admin/users");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<(string DeviceCode, string UserCode, string VerificationUri)> RequestDeviceCodeAsync(
        HttpClient client, string scope)
    {
        using var response = await client.PostAsync("/connect/device", new FormUrlEncodedContent(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["client_id"] = DeviceClientId,
                ["scope"] = scope,
            }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (
            document.RootElement.GetProperty("device_code").GetString()!,
            document.RootElement.GetProperty("user_code").GetString()!,
            document.RootElement.GetProperty("verification_uri").GetString()!);
    }

    /// <summary>Drives the JSON <c>/connect/verify</c> contract exactly as a native/SPA client would: GET for the antiforgery token, then POST the decision.</summary>
    private static async Task ApproveOrDenyViaJsonApiAsync(HttpClient signedInClient, string userCode, bool accept)
    {
        using var getResponse = await signedInClient.GetAsync($"/connect/verify?user_code={userCode}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        using var getDocument = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync());
        var antiforgeryToken = getDocument.RootElement.GetProperty("antiforgeryToken").GetString()!;

        using var postResponse = await signedInClient.PostAsync($"/connect/verify?user_code={userCode}",
            new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["user_code"] = userCode,
                ["accept"] = accept ? "true" : "false",
                ["__RequestVerificationToken"] = antiforgeryToken,
            }));
        // OpenIddict's own authentication handler intercepts Results.Forbid(...) for its scheme and responds
        // with its own OAuth-shaped error rather than a plain ASP.NET Core 403 — for a denied verification
        // that's a 400 with an "access_denied" body, matching the RFC 6749/8628 convention that most OAuth
        // protocol errors (as opposed to a generic authorization failure) use 400, not 403. The real
        // assertion that matters is the subsequent /connect/token poll below.
        var postBody = await postResponse.Content.ReadAsStringAsync();
        Assert.True(postResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.Forbidden or HttpStatusCode.BadRequest,
            $"Unexpected status {postResponse.StatusCode} approving/denying a device code: {postBody}");
    }

    private static async Task<(string? AccessToken, string? Error)> PollTokenOnceAsync(HttpClient client, string deviceCode)
    {
        using var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["device_code"] = deviceCode,
                ["client_id"] = DeviceClientId,
            }));
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return response.IsSuccessStatusCode
            ? (document.RootElement.GetProperty("access_token").GetString(), null)
            : (null, document.RootElement.GetProperty("error").GetString());
    }
}
