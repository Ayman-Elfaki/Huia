using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Huia.Events;
using Huia.IntegrationTests.Infrastructure;

namespace Huia.IntegrationTests;

/// <summary>
/// End-to-end coverage of the passkey (WebAuthn) endpoints using an in-process software authenticator
/// (<see cref="SoftwarePasskey"/>): discoverable primary sign-in, credential management, passkey as a
/// second factor, recovery codes and tenant isolation.
/// </summary>
public sealed partial class PasskeyFlowTests : IAsyncLifetime
{
    private const string RedirectUri = "https://acme.example.test/callback";
    private const string Origin = "http://localhost";

    private HuiaTestHost _host = null!;
    private string _daveId = null!;

    public async Task InitializeAsync()
    {
        _host = await HuiaTestHost.StartAsync(configureOptions: huia =>
            huia.AddTenant("pkother", tenant =>
            {
                tenant.Authentication.UseEmailAndPasswordLogin(password => password.RequireConfirmedEmail = false);
                tenant.Authentication.UsePasskeyLogin();
            }));

        await _host.SeedInteractiveClientAsync("acme", "acme-spa", RedirectUri);
        _daveId = await _host.SeedUserAsync("acme", "dave@acme.test", "Password1!", emailConfirmed: true);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task A_registered_passkey_signs_in_discoverably_and_the_session_completes_an_authorize()
    {
        var passkey = new SoftwarePasskey(Origin);
        var api = await BearerClientAsync();
        await RegisterPasskeyAsync(api, "acme", passkey, "Test key");

        var browser = _host.CreateClient();
        var token = await AntiforgeryTokenAsync(browser, "acme");

        var optionsJson = await CeremonyAsync(browser, token, "/acme/identity/account/passkey/assertion-options", new { });
        var assertion = await browser.SendAsync(CeremonyRequest(token,
            "/acme/identity/account/passkey/assertion",
            new { credential = JsonDocument.Parse(passkey.CreateAssertion(optionsJson, _daveId)).RootElement, returnUrl = "/acme/", rememberMe = false }));

        assertion.StatusCode.ShouldBe(HttpStatusCode.OK, await assertion.Content.ReadAsStringAsync());
        (await assertion.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("redirectUrl").GetString().ShouldNotBeNullOrEmpty();

        (await _host.Events.WaitForAsync<UserLoggedInEvent>(e => e.UserId == _daveId && e.Method == "passkey")).ShouldNotBeNull();

        // The cookie the assertion issued is a real session: /connect/authorize completes without a login redirect.
        var authorize = await browser.GetAsync(AuthorizeUrl("acme", "acme-spa"));
        authorize.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        authorize.Headers.Location!.ToString().ShouldStartWith(RedirectUri);
    }

    [Fact]
    public async Task An_assertion_whose_signature_counter_did_not_advance_is_rejected_as_a_replay()
    {
        var passkey = new SoftwarePasskey(Origin);
        var api = await BearerClientAsync();
        await RegisterPasskeyAsync(api, "acme", passkey, "Replay key");

        var browser = _host.CreateClient();
        var token = await AntiforgeryTokenAsync(browser, "acme");

        // First assertion advances the stored counter to 1 and signs in.
        var first = await CeremonyAsync(browser, token, "/acme/identity/account/passkey/assertion-options", new { });
        (await browser.SendAsync(CeremonyRequest(token, "/acme/identity/account/passkey/assertion",
            new { credential = JsonDocument.Parse(passkey.CreateAssertion(first, _daveId)).RootElement, returnUrl = "/acme/", rememberMe = false })))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // A fresh ceremony (with an antiforgery token re-issued for the now-authenticated session), but
        // the authenticator's counter is frozen — the server must reject it.
        var token2 = await AntiforgeryTokenAsync(browser, "acme");
        var second = await CeremonyAsync(browser, token2, "/acme/identity/account/passkey/assertion-options", new { });
        var replay = await browser.SendAsync(CeremonyRequest(token2, "/acme/identity/account/passkey/assertion",
            new { credential = JsonDocument.Parse(passkey.CreateAssertion(second, _daveId, advanceSignCount: false)).RootElement, returnUrl = "/acme/", rememberMe = false }));

        replay.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_passkey_registered_for_one_tenant_is_rejected_by_another()
    {
        var passkey = new SoftwarePasskey(Origin);
        var api = await BearerClientAsync();
        await RegisterPasskeyAsync(api, "acme", passkey, "Cross-tenant key");

        var browser = _host.CreateClient();
        var token = await AntiforgeryTokenAsync(browser, "pkother");

        var optionsJson = await CeremonyAsync(browser, token, "/pkother/identity/account/passkey/assertion-options", new { });
        var assertion = await browser.SendAsync(CeremonyRequest(token,
            "/pkother/identity/account/passkey/assertion",
            new { credential = JsonDocument.Parse(passkey.CreateAssertion(optionsJson, _daveId)).RootElement, returnUrl = "/pkother/", rememberMe = false }));

        assertion.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_passkey_can_be_listed_renamed_and_removed()
    {
        var passkey = new SoftwarePasskey(Origin);
        var api = await BearerClientAsync();
        await RegisterPasskeyAsync(api, "acme", passkey, "First name");

        var list = await api.GetFromJsonAsync<JsonElement>("/acme/manage/passkeys");
        var row = list.EnumerateArray().Single();
        row.GetProperty("name").GetString().ShouldBe("First name");
        var id = row.GetProperty("id").GetString()!;
        id.ShouldBe(passkey.CredentialIdB64);

        var rename = await api.PatchAsJsonAsync($"/acme/manage/passkeys/{id}", new { name = "Renamed" });
        rename.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await api.GetFromJsonAsync<JsonElement>("/acme/manage/passkeys"))
            .EnumerateArray().Single().GetProperty("name").GetString().ShouldBe("Renamed");

        var delete = await api.DeleteAsync($"/acme/manage/passkeys/{id}");
        delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await api.GetFromJsonAsync<JsonElement>("/acme/manage/passkeys")).GetArrayLength().ShouldBe(0);

        (await _host.Events.WaitForAsync<PasskeyRegisteredEvent>(e => e.UserId == _daveId)).ShouldNotBeNull();
        (await _host.Events.WaitForAsync<PasskeyRemovedEvent>(e => e.UserId == _daveId)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Enabling_the_second_factor_requires_a_passkey_and_returns_recovery_codes()
    {
        var api = await BearerClientAsync();

        var tooEarly = await api.PutAsJsonAsync("/acme/manage/passkeys/two-factor", new { enabled = true });
        tooEarly.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        await RegisterPasskeyAsync(api, "acme", new SoftwarePasskey(Origin), "2FA key");

        var enable = await api.PutAsJsonAsync("/acme/manage/passkeys/two-factor", new { enabled = true });
        enable.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await enable.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("enabled").GetBoolean().ShouldBeTrue();
        body.GetProperty("recoveryCodes").GetArrayLength().ShouldBe(10);

        (await api.GetFromJsonAsync<JsonElement>("/acme/manage/passkeys/two-factor"))
            .GetProperty("enabled").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task The_last_passkey_cannot_be_removed_while_it_is_the_second_factor()
    {
        var passkey = new SoftwarePasskey(Origin);
        var api = await BearerClientAsync();
        await RegisterPasskeyAsync(api, "acme", passkey, "Only key");
        (await api.PutAsJsonAsync("/acme/manage/passkeys/two-factor", new { enabled = true })).EnsureSuccessStatusCode();

        var delete = await api.DeleteAsync($"/acme/manage/passkeys/{passkey.CredentialIdB64}");
        delete.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_password_sign_in_steps_up_to_a_passkey_second_factor()
    {
        var passkey = new SoftwarePasskey(Origin);
        var api = await BearerClientAsync();
        await RegisterPasskeyAsync(api, "acme", passkey, "Step-up key");
        (await api.PutAsJsonAsync("/acme/manage/passkeys/two-factor", new { enabled = true })).EnsureSuccessStatusCode();

        var browser = _host.CreateClient();
        var loginUrl = await StartPasswordLoginAsync(browser, "acme", "acme-spa");
        var pageToken = ExtractToken(await browser.GetStringAsync(loginUrl));

        var post = await browser.PostAsync(loginUrl, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = "dave@acme.test",
            ["Input.Password"] = "Password1!",
            ["Input.RememberMe"] = "false",
            ["__RequestVerificationToken"] = pageToken,
        }));

        post.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        post.Headers.Location!.ToString().ShouldContain("LoginWith2fa", Case.Insensitive);

        var twoFaHtml = await browser.GetStringAsync(MakeLocal(post.Headers.Location!.ToString()));
        var flow = ExtractFlow(twoFaHtml);
        var afToken = ExtractToken(twoFaHtml);

        var optionsJson = await CeremonyAsync(browser, afToken, "/acme/identity/account/passkey/2fa-options", new { flow });
        var stepUp = await browser.SendAsync(CeremonyRequest(afToken, "/acme/identity/account/passkey/2fa",
            new { credential = JsonDocument.Parse(passkey.CreateAssertion(optionsJson, _daveId)).RootElement, flow, rememberMachine = false }));

        stepUp.StatusCode.ShouldBe(HttpStatusCode.OK, await stepUp.Content.ReadAsStringAsync());

        (await _host.Events.WaitForAsync<UserLoggedInEvent>(e => e.UserId == _daveId && e.Method == "mfa")).ShouldNotBeNull();

        var authorize = await browser.GetAsync(AuthorizeUrl("acme", "acme-spa"));
        authorize.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        authorize.Headers.Location!.ToString().ShouldStartWith(RedirectUri);
    }

    [Fact]
    public async Task A_recovery_code_completes_the_second_factor_when_the_passkey_is_unavailable()
    {
        var api = await BearerClientAsync();
        await RegisterPasskeyAsync(api, "acme", new SoftwarePasskey(Origin), "Recovery key");
        var enable = await (await api.PutAsJsonAsync("/acme/manage/passkeys/two-factor", new { enabled = true }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var recoveryCode = enable.GetProperty("recoveryCodes")[0].GetString()!;

        var browser = _host.CreateClient();
        var loginUrl = await StartPasswordLoginAsync(browser, "acme", "acme-spa");
        var pageToken = ExtractToken(await browser.GetStringAsync(loginUrl));
        var post = await browser.PostAsync(loginUrl, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = "dave@acme.test",
            ["Input.Password"] = "Password1!",
            ["__RequestVerificationToken"] = pageToken,
        }));

        var twoFaHtml = await browser.GetStringAsync(MakeLocal(post.Headers.Location!.ToString()));
        var recoverPost = await browser.PostAsync("/acme/identity/account/loginwith2fa?handler=RecoveryCode", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Flow"] = ExtractFlow(twoFaHtml),
            ["RememberMe"] = "false",
            ["Input.RecoveryCode"] = recoveryCode,
            ["__RequestVerificationToken"] = ExtractToken(twoFaHtml),
        }));

        recoverPost.StatusCode.ShouldBe(HttpStatusCode.Redirect, await recoverPost.Content.ReadAsStringAsync());
        recoverPost.Headers.Location!.ToString().ShouldNotContain("loginwith2fa", Case.Insensitive);
    }

    // --- helpers ---------------------------------------------------------------------------------

    private async Task<HttpClient> BearerClientAsync()
    {
        var flow = new AuthCodeFlow(_host, "acme", "acme-spa", RedirectUri);
        using var tokens = await flow.SignInAsync("dave@acme.test", "Password1!");
        var accessToken = tokens.RootElement.GetProperty("access_token").GetString()!;

        var api = _host.CreateClient();
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return api;
    }

    private static async Task RegisterPasskeyAsync(HttpClient api, string tenant, SoftwarePasskey passkey, string name)
    {
        var optionsJson = await (await api.SendAsync(WithOrigin(new HttpRequestMessage(
            HttpMethod.Post, $"/{tenant}/manage/passkeys/creation-options") { Content = JsonContent.Create(new { }) })))
            .Content.ReadAsStringAsync();

        var register = await api.SendAsync(WithOrigin(new HttpRequestMessage(HttpMethod.Post, $"/{tenant}/manage/passkeys")
        {
            Content = JsonContent.Create(new
            {
                credential = JsonDocument.Parse(passkey.CreateAttestation(optionsJson)).RootElement,
                name,
            }),
        }));
        register.StatusCode.ShouldBe(HttpStatusCode.OK, await register.Content.ReadAsStringAsync());
    }

    private static HttpRequestMessage WithOrigin(HttpRequestMessage request)
    {
        request.Headers.Add("Origin", Origin);
        return request;
    }

    private async Task<string> AntiforgeryTokenAsync(HttpClient client, string tenant)
    {
        var html = await client.GetStringAsync($"/{tenant}/identity/account/login");
        return ExtractToken(html);
    }

    private static HttpRequestMessage CeremonyRequest(string token, string path, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("X-Huia-CSRF", token);
        request.Headers.Add("Origin", Origin);
        return request;
    }

    private static async Task<string> CeremonyAsync(HttpClient client, string token, string path, object body)
    {
        var response = await client.SendAsync(CeremonyRequest(token, path, body));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadAsStringAsync();
    }

    private async Task<string> StartPasswordLoginAsync(HttpClient browser, string tenant, string clientId)
    {
        var toLogin = await browser.GetAsync(AuthorizeUrl(tenant, clientId));
        return MakeLocal(toLogin.Headers.Location!.ToString());
    }

    private static string AuthorizeUrl(string tenant, string clientId) =>
        $"/{tenant}/connect/authorize?response_type=code&client_id={clientId}" +
        $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}&scope=openid&code_challenge=x&code_challenge_method=plain&state=s";

    private static string MakeLocal(string location) =>
        location.StartsWith('/') ? location : new Uri(new Uri("http://localhost"), location).PathAndQuery;

    private static string ExtractToken(string html) =>
        TokenRegex().Match(html) is { Success: true } m ? m.Groups["v"].Value : throw new InvalidOperationException("no antiforgery token");

    private static string ExtractFlow(string html) =>
        FlowRegex().Match(html) is { Success: true } m ? m.Groups["v"].Value : throw new InvalidOperationException("no flow token");

    [GeneratedRegex("""name="__RequestVerificationToken"[^>]*value="(?<v>[^"]+)""")]
    private static partial Regex TokenRegex();

    [GeneratedRegex("""name="Flow"[^>]*value="(?<v>[^"]*)""")]
    private static partial Regex FlowRegex();
}
