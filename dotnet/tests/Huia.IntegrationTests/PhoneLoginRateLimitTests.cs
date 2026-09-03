using System.Net;
using Huia.AspNetCore.Services;
using Huia.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.IntegrationTests;

/// <summary>
/// Covers the <em>successful</em>-sign-in throttle for phone login (distinct from code-request
/// throttling): once per <c>SuccessfulLoginWindow</c> and an absolute daily ceiling, both configurable
/// on <c>PhoneLoginOptions</c>.
/// </summary>
public sealed class PhoneLoginRateLimitTests
{
    private static int _counter;

    private static string FreshNumber() => "+1500556" + Interlocked.Increment(ref _counter).ToString("D4");

    [Fact]
    public async Task A_second_sign_in_inside_the_window_is_blocked_with_the_cooldown_message()
    {
        await using var host = await HuiaTestHost.StartAsync(configureOptions: huia =>
            huia.AddTenant("phone-rl", tenant =>
            {
                tenant.Authentication.Password.RequireConfirmedEmail = false;
                tenant.Authentication.UsePasswordlessFlow(pwl => pwl.UsePhoneLogin());
            }));

        var number = FreshNumber();
        await host.SeedUserAsync("phone-rl", $"{number}@phone.test", password: null, phoneNumber: number);

        var first = await SignInAsync(host, "phone-rl", number);
        first.StatusCode.ShouldBe(HttpStatusCode.Redirect, await BodyAsync(first));

        var second = await SignInAsync(host, "phone-rl", number);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await second.Content.ReadAsStringAsync()).ShouldContain("very recently");
    }

    [Fact]
    public async Task Raising_the_window_allowance_lets_the_next_sign_in_through()
    {
        await using var host = await HuiaTestHost.StartAsync(configureOptions: huia =>
            huia.AddTenant("phone-rl-cfg", tenant =>
            {
                tenant.Authentication.Password.RequireConfirmedEmail = false;
                tenant.Authentication.UsePasswordlessFlow(pwl =>
                    pwl.UsePhoneLogin(phone => phone.SuccessfulLoginsPerWindow = 2));
            }));

        var number = FreshNumber();
        await host.SeedUserAsync("phone-rl-cfg", $"{number}@phone.test", password: null, phoneNumber: number);

        (await SignInAsync(host, "phone-rl-cfg", number)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await SignInAsync(host, "phone-rl-cfg", number)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await SignInAsync(host, "phone-rl-cfg", number)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_daily_ceiling_blocks_once_the_short_window_has_replenished()
    {
        await using var host = await HuiaTestHost.StartAsync(configureOptions: huia =>
            huia.AddTenant("phone-rl-daily", tenant =>
            {
                tenant.Authentication.Password.RequireConfirmedEmail = false;
                tenant.Authentication.UsePasswordlessFlow(pwl => pwl.UsePhoneLogin(phone =>
                {
                    // A tiny window that replenishes between calls, so only the daily ceiling can bite.
                    phone.SuccessfulLoginsPerWindow = 3;
                    phone.SuccessfulLoginWindow = TimeSpan.FromMilliseconds(100);
                    phone.SuccessfulLoginsPerDay = 3;
                }));
            }));

        var limiter = host.Services.GetRequiredService<IPhoneLoginRateLimiter>();
        var number = FreshNumber();

        for (var i = 0; i < 3; i++)
        {
            limiter.TryRecordLogin("phone-rl-daily", number, out _, out _).ShouldBeTrue($"call #{i + 1}");
        }

        // Let the fixed window fully replenish; the rolling day does not.
        await Task.Delay(400);

        limiter.TryRecordLogin("phone-rl-daily", number, out var retryAfter, out var dailyLimitReached).ShouldBeFalse();
        dailyLimitReached.ShouldBeTrue();
        retryAfter.ShouldBeNull();
    }

    [Fact]
    public async Task CanRecordLogin_reports_the_limit_without_consuming_a_permit()
    {
        await using var host = await HuiaTestHost.StartAsync(configureOptions: huia =>
            huia.AddTenant("phone-rl-peek", tenant =>
            {
                tenant.Authentication.Password.RequireConfirmedEmail = false;
                tenant.Authentication.UsePasswordlessFlow(pwl => pwl.UsePhoneLogin());
            }));

        var limiter = host.Services.GetRequiredService<IPhoneLoginRateLimiter>();
        var number = FreshNumber();

        // Consume the single per-window permit.
        limiter.TryRecordLogin("phone-rl-peek", number, out _, out _).ShouldBeTrue();

        // Peeking now reports "blocked" — repeatedly — and must not itself consume anything.
        for (var i = 0; i < 3; i++)
        {
            limiter.CanRecordLogin("phone-rl-peek", number, out var retryAfter, out var daily).ShouldBeFalse();
            daily.ShouldBeFalse();
            retryAfter.ShouldNotBeNull();
        }

        // A different number is still free (proves the peeks did not leak into a shared counter).
        limiter.CanRecordLogin("phone-rl-peek", FreshNumber(), out _, out _).ShouldBeTrue();
    }

    [Fact]
    public async Task A_different_number_on_the_same_tenant_is_not_throttled()
    {
        await using var host = await HuiaTestHost.StartAsync(configureOptions: huia =>
            huia.AddTenant("phone-rl-iso", tenant =>
            {
                tenant.Authentication.Password.RequireConfirmedEmail = false;
                tenant.Authentication.UsePasswordlessFlow(pwl => pwl.UsePhoneLogin());
            }));

        var first = FreshNumber();
        var second = FreshNumber();
        await host.SeedUserAsync("phone-rl-iso", $"{first}@phone.test", password: null, phoneNumber: first);
        await host.SeedUserAsync("phone-rl-iso", $"{second}@phone.test", password: null, phoneNumber: second);

        (await SignInAsync(host, "phone-rl-iso", first)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await SignInAsync(host, "phone-rl-iso", second)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    private static async Task<HttpResponseMessage> SignInAsync(HuiaTestHost host, string tenant, string number)
    {
        var flow = new PhoneFlow(host, tenant);

        // The successful-sign-in ceiling is now checked before the code is sent, so a blocked attempt
        // re-renders the login page (200) instead of redirecting to VerifyOtp.
        var request = await flow.RequestCodeRawAsync(number);
        if (request.StatusCode != HttpStatusCode.Redirect)
        {
            return request;
        }

        var token = PhoneFlow.FlowTokenOf(request.Headers.Location!.ToString());
        var code = host.Sms.LastCode(number);
        code.ShouldNotBeNull();
        return await flow.SubmitCodeAsync(token, code!);
    }

    private static async Task<string> BodyAsync(HttpResponseMessage response)
    {
        try
        {
            return await response.Content.ReadAsStringAsync();
        }
        catch
        {
            return string.Empty;
        }
    }
}
