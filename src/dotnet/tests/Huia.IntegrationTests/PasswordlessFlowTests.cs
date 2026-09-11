using System.Net;
using Huia.EntityFrameworkCore;
using Huia.EntityFrameworkCore.Entities;
using Huia.Identity;
using Huia.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.IntegrationTests;

public sealed class PasswordlessFlowTests : IAsyncLifetime
{
    private static int _counter;
    private HuiaTestHost _host = null!;

    public async Task InitializeAsync() => _host = await HuiaTestHost.StartAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private static string FreshNumber() => "+1500555" + Interlocked.Increment(ref _counter).ToString("D4");

    [Fact]
    public async Task The_standalone_phone_login_page_is_gone()
    {
        var response = await _host.Client.GetAsync("/phone/identity/account/phonelogin");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_existing_phone_user_signs_in_with_a_one_time_code()
    {
        var number = FreshNumber();
        await _host.SeedUserAsync("phone", $"{number}@phone.test", password: null, emailConfirmed: true, phoneNumber: number);

        var flow = new PhoneFlow(_host, "phone");
        var token = await flow.RequestCodeAsync(number);

        var code = _host.Sms.LastCode(number);
        code.ShouldNotBeNull();

        var response = await flow.SubmitCodeAsync(token, code!);
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await flow.IsSignedInAsync()).ShouldBeTrue();
    }

    [Fact]
    public async Task A_wrong_code_is_rejected_and_the_code_survives_for_another_try()
    {
        var number = FreshNumber();
        await _host.SeedUserAsync("phone", $"{number}@phone.test", password: null, phoneNumber: number);

        var flow = new PhoneFlow(_host, "phone");
        var token = await flow.RequestCodeAsync(number);
        var realCode = _host.Sms.LastCode(number)!;

        var bad = await flow.SubmitCodeAsync(token, "000000" == realCode ? "111111" : "000000");
        bad.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await bad.Content.ReadAsStringAsync()).ShouldContain("not correct");

        var good = await flow.SubmitCodeAsync(token, realCode);
        good.StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task An_unknown_number_without_auto_provisioning_never_yields_a_code()
    {
        var number = FreshNumber();

        var flow = new PhoneFlow(_host, "phone");
        await flow.RequestCodeAsync(number); // still redirects to verify (no enumeration)

        _host.Sms.LastCode(number).ShouldBeNull();
    }

    [Fact]
    public async Task Auto_provisioning_defers_account_creation_until_the_profile_is_completed()
    {
        var number = FreshNumber();
        var flow = new PhoneFlow(_host, "phone-auto");

        var token = await flow.RequestCodeAsync(number);
        var code = _host.Sms.LastCode(number);
        code.ShouldNotBeNull();

        // No user exists yet.
        (await CountUsersAsync("phone-auto", number)).ShouldBe(0);

        var afterCode = await flow.SubmitCodeAsync(token, code!);
        afterCode.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        flow.LastLocation!.ShouldContain("completeprofile");

        var completeFlow = ExtractQuery(flow.LastLocation!, "flow")!;

        var page = await _host.CreateClient().GetStringAsync(
            $"/phone-auto/identity/account/completeprofile?flow={Uri.EscapeDataString(completeFlow)}");
        page.ShouldContain("data-validate");
        page.ShouldContain("js/form-validate.js");
        page.ShouldContain("data-val-for=\"Input.FirstName\"");
        page.ShouldContain("required");

        var afterProfile = await flow.CompleteProfileAsync(completeFlow, "Sam", "Rivera");
        afterProfile.StatusCode.ShouldBe(HttpStatusCode.Redirect);

        (await CountUsersAsync("phone-auto", number)).ShouldBe(1);
    }

    private async Task<int> CountUsersAsync(string tenantId, string phoneNumber)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HuiaDbContext>();
        // No tenant is in scope here, so bypass the per-tenant query filter and match TenantId explicitly.
        return await db.Set<HuiaUser>().IgnoreQueryFilters()
            .CountAsync(u => u.TenantId == tenantId && u.PhoneNumber == phoneNumber);
    }

    private static string? ExtractQuery(string url, string key)
    {
        var q = url.Contains('?', StringComparison.Ordinal) ? url[(url.IndexOf('?', StringComparison.Ordinal) + 1)..] : string.Empty;
        foreach (var pair in q.Split('&'))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0] == key)
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        return null;
    }
}
