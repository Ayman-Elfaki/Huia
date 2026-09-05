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
/// (<see cref="SoftwarePasskey"/>): discoverable primary sign-in, credential management, the
/// post-sign-up enrollment prompt and tenant isolation.
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
        {
            huia.AddTenant("pkother", tenant =>
            {
                tenant.Authentication.UseEmailAndPasswordLogin(password => password.RequireConfirmedEmail = false);
                tenant.Authentication.UsePasskeyLogin();
            });
            huia.AddTenant("pkrp", tenant =>
            {
                tenant.Authentication.UseEmailAndPasswordLogin(password => password.RequireConfirmedEmail = false);
                tenant.Authentication.UsePasskeyLogin(passkey =>
                {
                    passkey.RelyingPartyId = "pkrp.localhost";
                    passkey.AllowedOrigins.Add("http://pkrp.localhost");
                });
            });
        });

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
    public async Task Creation_options_require_a_discoverable_resident_key()
    {
        var api = await BearerClientAsync();
        var json = await (await api.SendAsync(WithOrigin(new HttpRequestMessage(
            HttpMethod.Post, "/acme/manage/passkeys/creation-options") { Content = JsonContent.Create(new { }) })))
            .Content.ReadFromJsonAsync<JsonElement>();

        json.GetProperty("authenticatorSelection").GetProperty("residentKey").GetString().ShouldBe("required");
    }

    [Fact]
    public async Task A_per_tenant_relying_party_id_is_reflected_in_the_ceremony_options()
    {
        var browser = _host.CreateClient();
        var token = await AntiforgeryTokenAsync(browser, "pkrp");

        var options = await CeremonyAsync(browser, token, "/pkrp/identity/account/passkey/assertion-options", new { });

        JsonDocument.Parse(options).RootElement.GetProperty("rpId").GetString().ShouldBe("pkrp.localhost");
    }

    [Fact]
    public async Task A_first_sign_up_is_redirected_to_the_passkey_enrollment_prompt()
    {
        var browser = _host.CreateClient();
        var register = await RegisterViaUiAsync(browser, "acme", "newbie@acme.test", "Password1!");

        register.StatusCode.ShouldBe(HttpStatusCode.Redirect, await register.Content.ReadAsStringAsync());
        register.Headers.Location!.ToString().ShouldContain("passkeyenroll", Case.Insensitive);
    }

    [Fact]
    public async Task Skipping_enrollment_returns_to_the_flow_and_is_not_prompted_again()
    {
        var browser = _host.CreateClient();
        var toEnroll = await RegisterViaUiAsync(browser, "acme", "skipper@acme.test", "Password1!");
        var enrollUrl = MakeLocal(toEnroll.Headers.Location!.ToString());
        var enrollHtml = await browser.GetStringAsync(enrollUrl);

        var skip = await browser.PostAsync("/acme/identity/account/passkeyenroll?handler=Skip", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["ReturnUrl"] = "/acme/",
            ["__RequestVerificationToken"] = ExtractToken(enrollHtml),
        }));
        skip.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        skip.Headers.Location!.ToString().ShouldNotContain("passkeyenroll", Case.Insensitive);

        // Sign out and sign in again — the prompt is not shown a second time.
        await browser.PostAsync("/acme/identity/account/logout", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["__RequestVerificationToken"] = ExtractToken(await browser.GetStringAsync("/acme/identity/account/login")) }));

        var loginUrl = "/acme/identity/account/login";
        var loginPost = await browser.PostAsync(loginUrl, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = "skipper@acme.test",
            ["Input.Password"] = "Password1!",
            ["__RequestVerificationToken"] = ExtractToken(await browser.GetStringAsync(loginUrl)),
        }));

        loginPost.StatusCode.ShouldBe(HttpStatusCode.Redirect, await loginPost.Content.ReadAsStringAsync());
        loginPost.Headers.Location!.ToString().ShouldNotContain("passkeyenroll", Case.Insensitive);
    }

    [Fact]
    public async Task Enrollment_registers_a_credential()
    {
        var browser = _host.CreateClient();
        var toEnroll = await RegisterViaUiAsync(browser, "acme", "enroller@acme.test", "Password1!");
        var enrollUrl = MakeLocal(toEnroll.Headers.Location!.ToString());
        var token = ExtractToken(await browser.GetStringAsync(enrollUrl));

        var passkey = new SoftwarePasskey(Origin);
        var optionsJson = await CeremonyAsync(browser, token, "/acme/identity/account/passkeyenroll?handler=CreationOptions", new { });
        var register = await browser.SendAsync(CeremonyRequest(token, "/acme/identity/account/passkeyenroll?handler=Register",
            new { credential = JsonDocument.Parse(passkey.CreateAttestation(optionsJson)).RootElement, name = (string?)null }));

        register.StatusCode.ShouldBe(HttpStatusCode.OK, await register.Content.ReadAsStringAsync());
        (await register.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("redirectUrl").GetString().ShouldNotContain("passkeyenroll");

        var enrollerId = await _host.WithUserManagerAsync("acme", async um => (await um.FindByEmailAsync("enroller@acme.test"))!.Id);
        (await _host.Events.WaitForAsync<PasskeyRegisteredEvent>(e => e.UserId == enrollerId)).ShouldNotBeNull();
    }

    [Fact]
    public async Task A_user_that_already_has_a_passkey_is_not_prompted_at_sign_in()
    {
        var api = await BearerClientAsync();
        await RegisterPasskeyAsync(api, "acme", new SoftwarePasskey(Origin), "Existing key");

        var browser = _host.CreateClient();
        var loginUrl = "/acme/identity/account/login";
        var loginPost = await browser.PostAsync(loginUrl, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = "dave@acme.test",
            ["Input.Password"] = "Password1!",
            ["__RequestVerificationToken"] = ExtractToken(await browser.GetStringAsync(loginUrl)),
        }));

        loginPost.StatusCode.ShouldBe(HttpStatusCode.Redirect, await loginPost.Content.ReadAsStringAsync());
        loginPost.Headers.Location!.ToString().ShouldNotContain("passkeyenroll", Case.Insensitive);
    }

    [Fact]
    public async Task A_passkey_only_account_cannot_remove_its_last_passkey()
    {
        var loner = await _host.SeedUserAsync("acme", "loner@acme.test", password: null);
        await _host.WithUserManagerAsync("acme", async um =>
        {
            var u = (await um.FindByIdAsync(loner))!;
            await um.AddOrUpdatePasskeyAsync(u, new Microsoft.AspNetCore.Identity.UserPasskeyInfo(
                System.Security.Cryptography.RandomNumberGenerator.GetBytes(32), [1], DateTimeOffset.UtcNow, 0, null, true, true, true, [1], [1]) { Name = "only" });
            return true;
        });

        var canRemove = await _host.WithUserManagerAsync("acme", async um =>
            await um.CanRemovePasskeyAsync((await um.FindByIdAsync(loner))!));

        canRemove.ShouldBeFalse();
    }

    [Fact]
    public async Task The_login_page_gates_the_passkey_block_on_webauthn_before_paint()
    {
        var client = _host.CreateClient();
        var html = await client.GetStringAsync("/acme/identity/account/login");

        // The block is rendered normally (no `hidden` attribute) — visibility is decided pre-paint by
        // the head script + CSS, not by post-load JS.
        var openTag = html[html.IndexOf("data-huia-passkey", StringComparison.Ordinal)..];
        openTag = openTag[..openTag.IndexOf('>')];
        openTag.ShouldNotContain("hidden");
        html.ShouldContain("window.PublicKeyCredential");
    }

    // --- helpers ---------------------------------------------------------------------------------

    private async Task<HttpResponseMessage> RegisterViaUiAsync(HttpClient browser, string tenant, string email, string password)
    {
        var url = $"/{tenant}/identity/account/register";
        return await browser.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.FirstName"] = "New",
            ["Input.LastName"] = "User",
            ["Input.Email"] = email,
            ["Input.Password"] = password,
            ["Input.ConfirmPassword"] = password,
            ["__RequestVerificationToken"] = ExtractToken(await browser.GetStringAsync(url)),
        }));
    }

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

    private static string AuthorizeUrl(string tenant, string clientId) =>
        $"/{tenant}/connect/authorize?response_type=code&client_id={clientId}" +
        $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}&scope=openid&code_challenge=x&code_challenge_method=plain&state=s";

    private static string MakeLocal(string location) =>
        location.StartsWith('/') ? location : new Uri(new Uri("http://localhost"), location).PathAndQuery;

    private static string ExtractToken(string html) =>
        TokenRegex().Match(html) is { Success: true } m ? m.Groups["v"].Value : throw new InvalidOperationException("no antiforgery token");

    [GeneratedRegex("""name="__RequestVerificationToken"[^>]*value="(?<v>[^"]+)""")]
    private static partial Regex TokenRegex();
}
