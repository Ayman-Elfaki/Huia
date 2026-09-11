using Huia.Identity;
using Huia.OpenId.Identity;
using Huia.IntegrationTests.Infrastructure;

namespace Huia.IntegrationTests;

/// <summary>
/// Mirrors the option-facing cases in ASP.NET Core's <c>UserManagerTest</c> — password validation
/// against the policy, the lockout counter, and email-confirmation token round-tripping — exercised
/// through a <see cref="HuiaUserManager"/> resolved for the <see cref="HuiaAuthFlow.EmailAndPasswordLogin"/> flow.
/// </summary>
public sealed class HuiaUserManagerFlowTests : IAsyncLifetime
{
    private const string Tenant = "usermgrflow";

    private HuiaTestHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await HuiaTestHost.StartAsync(huia => huia.AddTenant(Tenant, tenant =>
        {
            tenant.Authentication.UseEmailAndPasswordLogin(password =>
            {
                password.MinimumLength = 14;
                password.RequireNonAlphanumeric = true;
                password.MaxFailedAccessAttempts = 5;
            });
        }));
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Create_rejects_a_password_below_the_tenant_minimum_length()
    {
        var result = await _host.WithFlowIdentityAsync(Tenant, HuiaAuthFlow.EmailAndPasswordLogin, async fi =>
            await fi.UserManager.CreateAsync(NewUser("short@usermgrflow.test"), "Abc1!def"));

        result.Succeeded.ShouldBeFalse();
        result.Errors.Select(e => e.Code).ShouldContain("PasswordTooShort");
    }

    [Fact]
    public async Task Create_rejects_a_password_that_misses_a_required_non_alphanumeric()
    {
        var result = await _host.WithFlowIdentityAsync(Tenant, HuiaAuthFlow.EmailAndPasswordLogin, async fi =>
            await fi.UserManager.CreateAsync(NewUser("plain@usermgrflow.test"), "Abcdefgh12345"));

        result.Succeeded.ShouldBeFalse();
        result.Errors.Select(e => e.Code).ShouldContain("PasswordRequiresNonAlphanumeric");
    }

    [Fact]
    public async Task Create_accepts_a_password_that_meets_the_whole_policy()
    {
        var result = await _host.WithFlowIdentityAsync(Tenant, HuiaAuthFlow.EmailAndPasswordLogin, async fi =>
            await fi.UserManager.CreateAsync(NewUser("ok@usermgrflow.test"), "Abcdefgh12345!"));

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task Access_failed_count_increments_below_the_flow_ceiling_then_resets()
    {
        var userId = await _host.SeedUserAsync(Tenant, "counter@usermgrflow.test", "Abcdefgh12345!", emailConfirmed: true);

        var (afterTwo, lockedOut, afterReset) = await _host.WithFlowIdentityAsync(Tenant, HuiaAuthFlow.EmailAndPasswordLogin, async fi =>
        {
            var user = (await fi.UserManager.FindByIdAsync(userId))!;
            await fi.UserManager.AccessFailedAsync(user);
            await fi.UserManager.AccessFailedAsync(user);
            var count = await fi.UserManager.GetAccessFailedCountAsync(user);
            var locked = await fi.UserManager.IsLockedOutAsync(user);
            await fi.UserManager.ResetAccessFailedCountAsync(user);
            return (count, locked, await fi.UserManager.GetAccessFailedCountAsync(user));
        });

        afterTwo.ShouldBe(2);
        lockedOut.ShouldBeFalse();
        afterReset.ShouldBe(0);
    }

    [Fact]
    public async Task Email_confirmation_token_round_trips_on_a_flow_scoped_manager()
    {
        var userId = await _host.SeedUserAsync(Tenant, "confirm@usermgrflow.test", "Abcdefgh12345!", emailConfirmed: false);

        var confirmed = await _host.WithFlowIdentityAsync(Tenant, HuiaAuthFlow.EmailAndPasswordLogin, async fi =>
        {
            var user = (await fi.UserManager.FindByIdAsync(userId))!;
            var token = await fi.UserManager.GenerateEmailConfirmationTokenAsync(user);
            token.ShouldNotBeNullOrEmpty();
            var result = await fi.UserManager.ConfirmEmailAsync(user, token);
            result.Succeeded.ShouldBeTrue();
            return await fi.UserManager.IsEmailConfirmedAsync((await fi.UserManager.FindByIdAsync(userId))!);
        });

        confirmed.ShouldBeTrue();
    }

    private static HuiaUser NewUser(string email) => new()
    {
        TenantId = Tenant,
        UserName = email,
        Email = email,
        FirstName = "Test",
        LastName = "User",
    };
}
