using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Huia.AspNetCore.Identity;
using Huia.IntegrationTests.Infrastructure;

namespace Huia.IntegrationTests;

/// <summary>
/// Contact-detail rules per account type: a password / external account owns an email and cannot set a
/// phone number; a phone-login account owns its number (which is also the username) and cannot set an
/// email or remove the number.
/// </summary>
public sealed class ManageContactRulesTests : IAsyncLifetime
{
    private const string RedirectUri = "https://phone.example.test/callback";
    private const string OriginalNumber = "+15005550301";

    private HuiaTestHost _host = null!;
    private HttpClient _phoneApi = null!;
    private string _phoneUserId = null!;

    public async Task InitializeAsync()
    {
        _host = await HuiaTestHost.StartAsync();
        await _host.SeedInteractiveClientAsync("phone", "phone-spa", RedirectUri);
        _phoneUserId = await _host.SeedPhoneUserAsync("phone", OriginalNumber);

        var flow = new PhoneFlow(_host, "phone");
        var token = await flow.RequestCodeAsync(OriginalNumber);
        var code = _host.Sms.LastCode(OriginalNumber);
        code.ShouldNotBeNull();
        var submit = await flow.SubmitCodeAsync(token, code!);
        submit.StatusCode.ShouldBe(HttpStatusCode.Redirect);

        var accessToken = await flow.GetAccessTokenAsync("phone-spa", RedirectUri);
        _phoneApi = _host.CreateClient();
        _phoneApi.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    public async Task DisposeAsync()
    {
        _phoneApi.Dispose();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task The_classifier_reports_each_account_type()
    {
        var passwordUserId = await _host.SeedUserAsync("phone", "pw@phone.test", "Password1!");
        var externalUserId = await _host.SeedUserAsync("phone", "ext@phone.test", password: null);
        await _host.AddExternalLoginAsync("phone", externalUserId, "Partner", "partner-key-1");

        var password = await _host.WithUserManagerAsync("phone", async um =>
            await um.GetUserTypeAsync((await um.FindByIdAsync(passwordUserId))!));
        var external = await _host.WithUserManagerAsync("phone", async um =>
            await um.GetUserTypeAsync((await um.FindByIdAsync(externalUserId))!));
        var phone = await _host.WithUserManagerAsync("phone", async um =>
            await um.GetUserTypeAsync((await um.FindByIdAsync(_phoneUserId))!));

        password.ShouldBe(HuiaUserType.Password);
        external.ShouldBe(HuiaUserType.External);
        phone.ShouldBe(HuiaUserType.Phone);
    }

    [Fact]
    public async Task A_phone_user_cannot_set_an_email()
    {
        var response = await _phoneApi.PutAsJsonAsync("/phone/manage/email", new { newEmail = "nope@phone.test" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_phone_user_cannot_remove_its_number()
    {
        var response = await _phoneApi.DeleteAsync("/phone/manage/phone");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_phone_user_changing_its_number_also_updates_the_username()
    {
        const string newNumber = "+15005550302";

        var start = await _phoneApi.PutAsJsonAsync("/phone/manage/phone", new { phoneNumber = newNumber });
        start.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var code = _host.Sms.LastCode(newNumber);
        code.ShouldNotBeNull();

        var confirm = await _phoneApi.PostAsJsonAsync("/phone/manage/phone/confirm", new { code });
        confirm.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var user = await _host.WithUserManagerAsync("phone", um => um.FindByIdAsync(_phoneUserId));
        user!.PhoneNumber.ShouldBe(newNumber);
        user.UserName.ShouldBe(newNumber);
    }
}
