using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Huia.IntegrationTests.Infrastructure;
using Huia.Options;

namespace Huia.IntegrationTests;

public sealed partial class DeviceFlowTests : IAsyncLifetime
{
    private const string DeviceCodeGrant = "urn:ietf:params:oauth:grant-type:device_code";

    private HuiaTestHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await HuiaTestHost.StartAsync(configureOptions: huia =>
        {
            huia.AddTenant("devices", tenant =>
            {
                tenant.Authentication.UseEmailAndPasswordLogin(password => password.RequireConfirmedEmail = false);
                tenant.AddDevice("device-cli", client => client.ClientSecret = "device-cli-secret");
            });
        });

        await _host.SeedUserAsync("devices", "dana@devices.test", "Password1!");
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task A_device_code_is_pending_until_the_user_approves_it_at_the_verification_endpoint()
    {
        var (deviceCode, userCode) = await StartDeviceAuthorizationAsync();

        var pending = await PollTokenAsync(deviceCode);
        pending.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorAsync(pending)).ShouldBe("authorization_pending");

        var browser = _host.CreateClient();
        await SignInAsync(browser, "dana@devices.test", "Password1!");
        await ApproveAsync(browser, userCode);

        var granted = await PollTokenAsync(deviceCode);
        granted.StatusCode.ShouldBe(HttpStatusCode.OK, await granted.Content.ReadAsStringAsync());
        using var tokens = JsonDocument.Parse(await granted.Content.ReadAsStringAsync());
        tokens.RootElement.TryGetProperty("access_token", out var accessToken).ShouldBeTrue();
        accessToken.GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Denying_at_the_verification_endpoint_leaves_the_device_code_unusable()
    {
        var (deviceCode, userCode) = await StartDeviceAuthorizationAsync();

        var browser = _host.CreateClient();
        await SignInAsync(browser, "dana@devices.test", "Password1!");

        var page = await GetVerificationPageAsync(browser, userCode);
        var deny = await browser.PostAsync("/devices/connect/verify", Form(new()
        {
            ["user_code"] = userCode,
            ["submit.deny"] = "deny",
            ["__RequestVerificationToken"] = ExtractAntiforgery(page),
        }));

        // OpenIddict surfaces the refusal at the verification endpoint (browser-facing, not JSON).
        (await deny.Content.ReadAsStringAsync()).ShouldContain("access_denied");

        var result = await PollTokenAsync(deviceCode);
        result.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorAsync(result)).ShouldBeOneOf("access_denied", "authorization_pending", "expired_token");
    }

    private async Task<(string DeviceCode, string UserCode)> StartDeviceAuthorizationAsync()
    {
        var response = await _host.Client.PostAsync("/devices/connect/device", Form(new()
        {
            ["client_id"] = "device-cli",
            ["client_secret"] = "device-cli-secret",
            ["scope"] = "openid profile offline_access",
        }));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (body.RootElement.GetProperty("device_code").GetString()!,
                body.RootElement.GetProperty("user_code").GetString()!);
    }

    private Task<HttpResponseMessage> PollTokenAsync(string deviceCode) =>
        _host.Client.PostAsync("/devices/connect/token", Form(new()
        {
            ["grant_type"] = DeviceCodeGrant,
            ["device_code"] = deviceCode,
            ["client_id"] = "device-cli",
            ["client_secret"] = "device-cli-secret",
        }));

    private async Task ApproveAsync(HttpClient browser, string userCode)
    {
        var page = await GetVerificationPageAsync(browser, userCode);
        var approve = await browser.PostAsync("/devices/connect/verify", Form(new()
        {
            ["user_code"] = userCode,
            ["submit.accept"] = "accept",
            ["__RequestVerificationToken"] = ExtractAntiforgery(page),
        }));
        approve.StatusCode.ShouldBeOneOf(HttpStatusCode.OK, HttpStatusCode.Redirect, HttpStatusCode.Found);
    }

    private async Task<string> GetVerificationPageAsync(HttpClient browser, string userCode)
    {
        var redirect = await browser.GetAsync($"/devices/connect/verify?user_code={Uri.EscapeDataString(userCode)}");
        redirect.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        redirect.Headers.Location!.ToString().ShouldContain("/identity/account/deviceverification");
        return await browser.GetStringAsync(MakeLocal(redirect.Headers.Location!.ToString()));
    }

    private static async Task SignInAsync(HttpClient client, string user, string password)
    {
        var page = await client.GetStringAsync("/devices/identity/account/login");
        var response = await client.PostAsync("/devices/identity/account/login", Form(new()
        {
            ["Input.Email"] = user,
            ["Input.Password"] = password,
            ["Input.RememberMe"] = "false",
            ["__RequestVerificationToken"] = ExtractAntiforgery(page),
        }));
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    private static FormUrlEncodedContent Form(Dictionary<string, string> values) => new(values);

    private static async Task<string?> ErrorAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetString();

    private static string MakeLocal(string location) =>
        location.StartsWith('/') ? location : new Uri(new Uri("http://localhost"), location).PathAndQuery;

    private static string ExtractAntiforgery(string html)
    {
        var match = AntiforgeryRegex().Match(html);
        if (!match.Success)
        {
            throw new InvalidOperationException("No antiforgery token on the page:\n" + (html.Length > 600 ? html[..600] : html));
        }

        return match.Groups["v"].Value;
    }

    [GeneratedRegex("""name="__RequestVerificationToken"[^>]*value="(?<v>[^"]+)""")]
    private static partial Regex AntiforgeryRegex();
}
