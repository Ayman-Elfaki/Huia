using System.Net.Http.Json;
using System.Text.Json;
using Huia.Headless.EntityFrameworkCore;
using Huia.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using HuiaDbContext = Huia.Headless.EntityFrameworkCore.HuiaDbContext<Huia.Entities.HuiaUser, Huia.Entities.HuiaRole, string>;

namespace Huia.IntegrationTests;

/// <summary>
/// Covers the JSON passwordless-SMS phone-login endpoints added to <c>Huia.Headless</c>
/// (<c>identity/phone/start</c> / <c>verify</c> / <c>complete-profile</c>) — the counterpart to
/// <c>Huia.OpenId</c>'s Razor <c>Login</c>/<c>VerifyOtp</c>/<c>CompleteProfile</c> pages, minus the page
/// navigation: a <see cref="Huia.Headless.Services.IPhoneLoginFlowStore"/> flow id plays the role the
/// encrypted flow token plays there.
/// </summary>
public sealed class HeadlessPhoneLoginTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static int _counter;

    private static string FreshNumber() => "+1500557" + Interlocked.Increment(ref _counter).ToString("D4");

    [Fact]
    public async Task A_new_number_signs_up_through_start_verify_and_complete_profile()
    {
        await using var host = await StartAsync();
        var number = FreshNumber();

        var start = await host.Client.PostAsJsonAsync("identity/phone/start", new { phoneNumber = number });
        start.EnsureSuccessStatusCode();
        var flowId = (await start.Content.ReadFromJsonAsync<StartResponse>(Json))!.FlowId;

        var code = host.Sms.LastCode(number);
        code.ShouldNotBeNull();

        var verify = await host.Client.PostAsJsonAsync("identity/phone/verify", new { flowId, code });
        verify.EnsureSuccessStatusCode();
        (await verify.Content.ReadFromJsonAsync<VerifyResponse>(Json))!.Status.ShouldBe("requires_profile");

        var complete = await host.Client.PostAsJsonAsync("identity/phone/complete-profile",
            new { flowId, firstName = "Percy", lastName = "Phone" });
        complete.EnsureSuccessStatusCode();
        var tokens = await complete.Content.ReadFromJsonAsync<TokenResponse>(Json);
        tokens!.AccessToken.ShouldNotBeNullOrEmpty();

        var me = new HttpRequestMessage(HttpMethod.Get, "identity/me");
        me.Headers.Add("Authorization", $"Bearer {tokens.AccessToken}");
        var meResponse = await host.Client.SendAsync(me);
        meResponse.EnsureSuccessStatusCode();
        var meBody = await meResponse.Content.ReadFromJsonAsync<MeResponse>(Json);
        meBody!.FirstName.ShouldBe("Percy");
        meBody.PhoneNumberConfirmed.ShouldBeTrue();
    }

    [Fact]
    public async Task An_existing_confirmed_user_signs_in_directly_without_a_profile_step()
    {
        await using var host = await StartAsync();
        var number = FreshNumber();
        await host.SeedPhoneUserAsync(number, "Nina", "Number");

        var start = await host.Client.PostAsJsonAsync("identity/phone/start", new { phoneNumber = number });
        start.EnsureSuccessStatusCode();
        var flowId = (await start.Content.ReadFromJsonAsync<StartResponse>(Json))!.FlowId;
        var code = host.Sms.LastCode(number);

        var verify = await host.Client.PostAsJsonAsync("identity/phone/verify", new { flowId, code });
        verify.EnsureSuccessStatusCode();

        // Already has a name — verify signs in directly (the response is the bearer token body, not
        // {status: "requires_profile"}).
        var tokens = await verify.Content.ReadFromJsonAsync<TokenResponse>(Json);
        tokens!.AccessToken.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task A_wrong_code_is_rejected_and_a_stale_flow_id_is_rejected()
    {
        await using var host = await StartAsync();
        var number = FreshNumber();

        var start = await host.Client.PostAsJsonAsync("identity/phone/start", new { phoneNumber = number });
        var flowId = (await start.Content.ReadFromJsonAsync<StartResponse>(Json))!.FlowId;

        var wrong = await host.Client.PostAsJsonAsync("identity/phone/verify", new { flowId, code = "000000" });
        wrong.IsSuccessStatusCode.ShouldBeFalse();

        var unknownFlow = await host.Client.PostAsJsonAsync("identity/phone/verify", new { flowId = "does-not-exist", code = "000000" });
        unknownFlow.IsSuccessStatusCode.ShouldBeFalse();
    }

    [Fact]
    public async Task An_invalid_phone_number_is_rejected_with_a_validation_problem()
    {
        await using var host = await StartAsync();

        var start = await host.Client.PostAsJsonAsync("identity/phone/start", new { phoneNumber = "not-a-number" });
        start.IsSuccessStatusCode.ShouldBeFalse();
    }

    [Fact]
    public async Task Start_reports_the_code_length_expiry_and_resend_window_and_a_masked_number()
    {
        await using var host = await StartAsync(phone =>
        {
            phone.CodeLength = 8;
            phone.CodeLifetime = TimeSpan.FromMinutes(3);
            phone.ResendCooldown = TimeSpan.FromSeconds(45);
        });
        var number = FreshNumber();

        var start = await host.Client.PostAsJsonAsync("identity/phone/start", new { phoneNumber = number });
        start.EnsureSuccessStatusCode();
        var body = (await start.Content.ReadFromJsonAsync<StartResponse>(Json))!;

        body.CodeLength.ShouldBe(8);
        body.ExpiresInSeconds.ShouldBe(180);
        body.ResendAvailableInSeconds.ShouldBe(45);
        body.MaskedPhoneNumber.ShouldNotContain(number);
        body.MaskedPhoneNumber.ShouldEndWith(number[^4..]);
        host.Sms.LastCode(number)!.Length.ShouldBe(8);
    }

    [Fact]
    public async Task Resend_inside_the_cooldown_is_rejected_with_retry_after()
    {
        await using var host = await StartAsync();
        var number = FreshNumber();
        var start = await host.Client.PostAsJsonAsync("identity/phone/start", new { phoneNumber = number });
        var flowId = (await start.Content.ReadFromJsonAsync<StartResponse>(Json))!.FlowId;

        var resend = await host.Client.PostAsJsonAsync("identity/phone/resend", new { flowId });

        resend.StatusCode.ShouldBe(System.Net.HttpStatusCode.TooManyRequests);
        var problem = (await resend.Content.ReadFromJsonAsync<ProblemResponse>(Json))!;
        problem.Error.ShouldBe("resend_too_soon");
        problem.RetryAfterSeconds.ShouldNotBeNull();
        problem.RetryAfterSeconds.Value.ShouldBeInRange(1, 30);
    }

    [Fact]
    public async Task Resend_after_the_cooldown_issues_a_new_code_that_replaces_the_old_one()
    {
        await using var host = await StartAsync(phone => phone.ResendCooldown = TimeSpan.Zero);
        var number = FreshNumber();
        await host.SeedPhoneUserAsync(number, "Rita", "Resend");

        var start = await host.Client.PostAsJsonAsync("identity/phone/start", new { phoneNumber = number });
        var flowId = (await start.Content.ReadFromJsonAsync<StartResponse>(Json))!.FlowId;
        var firstCode = host.Sms.LastCode(number)!;

        var resend = await host.Client.PostAsJsonAsync("identity/phone/resend", new { flowId });
        resend.EnsureSuccessStatusCode();
        (await resend.Content.ReadFromJsonAsync<StartResponse>(Json))!.FlowId.ShouldBe(flowId);
        var secondCode = host.Sms.LastCode(number)!;

        if (secondCode != firstCode)
        {
            var stale = await host.Client.PostAsJsonAsync("identity/phone/verify", new { flowId, code = firstCode });
            stale.IsSuccessStatusCode.ShouldBeFalse();
        }

        var verify = await host.Client.PostAsJsonAsync("identity/phone/verify", new { flowId, code = secondCode });
        verify.EnsureSuccessStatusCode();
        (await verify.Content.ReadFromJsonAsync<TokenResponse>(Json))!.AccessToken.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Resend_re_uses_the_pending_signup_so_a_new_number_can_still_complete()
    {
        await using var host = await StartAsync(phone => phone.ResendCooldown = TimeSpan.Zero);
        var number = FreshNumber();
        var start = await host.Client.PostAsJsonAsync("identity/phone/start", new { phoneNumber = number });
        var flowId = (await start.Content.ReadFromJsonAsync<StartResponse>(Json))!.FlowId;

        (await host.Client.PostAsJsonAsync("identity/phone/resend", new { flowId })).EnsureSuccessStatusCode();

        var verify = await host.Client.PostAsJsonAsync("identity/phone/verify", new { flowId, code = host.Sms.LastCode(number) });
        verify.EnsureSuccessStatusCode();
        (await verify.Content.ReadFromJsonAsync<VerifyResponse>(Json))!.Status.ShouldBe("requires_profile");
    }

    [Fact]
    public async Task Resend_for_an_unknown_flow_is_rejected()
    {
        await using var host = await StartAsync();

        var resend = await host.Client.PostAsJsonAsync("identity/phone/resend", new { flowId = "does-not-exist" });

        resend.StatusCode.ShouldBe(System.Net.HttpStatusCode.BadRequest);
        (await resend.Content.ReadFromJsonAsync<ProblemResponse>(Json))!.Error.ShouldBe("invalid");
    }

    [Fact]
    public async Task A_wrong_code_reports_the_attempts_remaining()
    {
        await using var host = await StartAsync(phone => phone.MaxVerificationAttempts = 3);
        var number = FreshNumber();
        var start = await host.Client.PostAsJsonAsync("identity/phone/start", new { phoneNumber = number });
        var flowId = (await start.Content.ReadFromJsonAsync<StartResponse>(Json))!.FlowId;
        var wrongCode = host.Sms.LastCode(number) == "000000" ? "111111" : "000000";

        var first = await host.Client.PostAsJsonAsync("identity/phone/verify", new { flowId, code = wrongCode });

        first.StatusCode.ShouldBe(System.Net.HttpStatusCode.BadRequest);
        var problem = (await first.Content.ReadFromJsonAsync<ProblemResponse>(Json))!;
        problem.Error.ShouldBe("invalid");
        problem.AttemptsRemaining.ShouldBe(2);
    }

    [Fact]
    public async Task An_unknown_number_without_auto_provisioning_gets_the_same_start_shape_as_a_known_one()
    {
        await using var host = await StartAsync(phone => phone.AllowAutoProvisioning = false);
        var known = FreshNumber();
        var unknown = FreshNumber();
        await host.SeedPhoneUserAsync(known, "Kim", "Known");

        var knownBody = (await (await host.Client.PostAsJsonAsync("identity/phone/start", new { phoneNumber = known }))
            .Content.ReadFromJsonAsync<StartResponse>(Json))!;
        var unknownBody = (await (await host.Client.PostAsJsonAsync("identity/phone/start", new { phoneNumber = unknown }))
            .Content.ReadFromJsonAsync<StartResponse>(Json))!;

        unknownBody.CodeLength.ShouldBe(knownBody.CodeLength);
        unknownBody.ExpiresInSeconds.ShouldBe(knownBody.ExpiresInSeconds);
        unknownBody.ResendAvailableInSeconds.ShouldBe(knownBody.ResendAvailableInSeconds);
        host.Sms.LastCode(unknown).ShouldBeNull();
    }

    private static async Task<HeadlessTestHost> StartAsync(Action<Huia.Options.PhoneAuthenticationMethod>? configurePhone = null)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var sms = new CapturingSmsSender();

        var builder = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton(connection);
                    services.AddDbContext<HuiaDbContext>(o => o.UseSqlite(connection));
                    services.AddSingleton<Huia.Services.ISmsSender>(sms);

                    services
                        .AddHuiaHeadless(huia =>
                        {
                            huia.UseIssuer("https://headless-phone.test");
                            huia.UseEmailAndPasswordLogin(p => p.RequireConfirmedEmail = false);
                            huia.UsePhoneLogin(phone =>
                            {
                                phone.AllowAutoProvisioning = true;
                                configurePhone?.Invoke(phone);
                            });
                        })
                        .AddEntityFrameworkCoreStores<HuiaDbContext>();
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapHuiaHeadlessEndpoints());
                });
            });

        var host = await builder.StartAsync();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<HuiaDbContext>().Database.EnsureCreatedAsync();
        }

        return new HeadlessTestHost(host, connection, sms);
    }

    private sealed class HeadlessTestHost(IHost host, SqliteConnection connection, CapturingSmsSender sms) : IAsyncDisposable
    {
        public HttpClient Client { get; } = host.GetTestClient();

        public CapturingSmsSender Sms { get; } = sms;

        public IServiceProvider Services => host.Services;

        public async Task SeedPhoneUserAsync(string phoneNumber, string firstName, string lastName)
        {
            await using var scope = Services.CreateAsyncScope();
            var userManager = scope.ServiceProvider.GetRequiredService<Huia.Headless.Identity.HuiaUserManager>();
            var (result, _) = await userManager.CreatePhoneUserAsync("phone", phoneNumber, firstName, lastName);
            result.Succeeded.ShouldBeTrue(string.Join(" ", result.Errors.Select(e => e.Description)));
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await host.StopAsync();
            host.Dispose();
            await connection.DisposeAsync();
        }
    }

    private sealed record StartResponse(
        string FlowId, string MaskedPhoneNumber, int CodeLength, int ExpiresInSeconds, int ResendAvailableInSeconds);

    private sealed record VerifyResponse(string FlowId, string MaskedPhoneNumber, string Status);

    private sealed record ProblemResponse(string Error, int? RetryAfterSeconds, int? AttemptsRemaining);

    private sealed record TokenResponse(string TokenType, string AccessToken, int ExpiresIn, string RefreshToken);

    private sealed record MeResponse(string Sub, string? Email, bool EmailConfirmed, string? PhoneNumber,
        bool PhoneNumberConfirmed, string FirstName, string LastName, string[] Roles);
}
