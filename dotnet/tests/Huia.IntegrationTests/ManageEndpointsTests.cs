using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Huia.Events;
using Huia.IntegrationTests.Infrastructure;

namespace Huia.IntegrationTests;

public sealed class ManageEndpointsTests : IAsyncLifetime
{
    private const string RedirectUri = "https://acme.example.test/callback";
    private HuiaTestHost _host = null!;
    private HttpClient _api = null!;
    private string _daveId = null!;

    public async Task InitializeAsync()
    {
        _host = await HuiaTestHost.StartAsync();
        await _host.SeedInteractiveClientAsync("acme", "acme-spa", RedirectUri);
        _daveId = await _host.SeedUserAsync("acme", "dave@acme.test", "Password1!", emailConfirmed: true);

        var flow = new AuthCodeFlow(_host, "acme", "acme-spa", RedirectUri);
        using var tokens = await flow.SignInAsync("dave@acme.test", "Password1!");
        var accessToken = tokens.RootElement.GetProperty("access_token").GetString()!;

        _api = _host.CreateClient();
        _api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    public async Task DisposeAsync()
    {
        _api.Dispose();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task An_anonymous_call_is_challenged()
    {
        var response = await _host.Client.GetAsync("/acme/manage/profile");
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Profile_can_be_read_and_updated_with_a_bearer_token()
    {
        var before = await _api.GetFromJsonAsync<JsonElement>("/acme/manage/profile");
        before.GetProperty("email").GetString().ShouldBe("dave@acme.test");

        var update = await _api.PutAsJsonAsync("/acme/manage/profile", new { firstName = "David", lastName = "Jones" });
        update.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = await _api.GetFromJsonAsync<JsonElement>("/acme/manage/profile");
        after.GetProperty("firstName").GetString().ShouldBe("David");
        after.GetProperty("lastName").GetString().ShouldBe("Jones");
    }

    [Fact]
    public async Task External_logins_can_be_listed_and_unlinked()
    {
        var before = await _api.GetFromJsonAsync<JsonElement>("/acme/manage/external-logins");
        before.GetProperty("logins").GetArrayLength().ShouldBe(0);
        before.GetProperty("canRemoveAny").GetBoolean().ShouldBeTrue(); // dave has a password

        await _host.AddExternalLoginAsync("acme", _daveId, "acme:Partner", "partner-subject-1");

        var after = await _api.GetFromJsonAsync<JsonElement>("/acme/manage/external-logins");
        var login = after.GetProperty("logins").EnumerateArray().Single();
        login.GetProperty("provider").GetString().ShouldBe("acme:Partner");
        login.GetProperty("shortName").GetString().ShouldBe("Partner");

        var remove = await _api.DeleteAsync("/acme/manage/external-logins/acme%3APartner/partner-subject-1");
        remove.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var empty = await _api.GetFromJsonAsync<JsonElement>("/acme/manage/external-logins");
        empty.GetProperty("logins").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task The_only_sign_in_method_cannot_be_unlinked()
    {
        // A user whose only credential is one external login.
        var loner = await _host.SeedUserAsync("acme", "loner@acme.test", password: null);
        await _host.AddExternalLoginAsync("acme", loner, "acme:Partner", "loner-subject");

        var canRemove = await _host.WithUserManagerAsync("acme", async um =>
            await Huia.AspNetCore.Areas.Identity.Pages.Account.ExternalLoginsModel.CanRemoveLoginAsync(
                um, (await um.FindByIdAsync(loner))!));

        canRemove.ShouldBeFalse();
    }

    [Fact]
    public async Task Password_can_be_changed()
    {
        var response = await _api.PutAsJsonAsync("/acme/manage/password", new { currentPassword = "Password1!", newPassword = "NewPassword2!" });
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var evt = await _host.Events.WaitForAsync<PasswordChangedEvent>(e => !e.Reset);
        evt.TenantId.ShouldBe("acme");
    }

    [Fact]
    public async Task A_password_user_cannot_add_a_phone_number()
    {
        var start = await _api.PutAsJsonAsync("/acme/manage/phone", new { phoneNumber = "+15005550123" });
        start.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var confirm = await _api.PostAsJsonAsync("/acme/manage/phone/confirm", new { code = "000000" });
        confirm.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
