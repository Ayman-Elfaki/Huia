using Huia.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity;
using Huia.IntegrationTests.Infrastructure;

namespace Huia.IntegrationTests;

/// <summary>
/// Mirrors the scenarios in ASP.NET Core's <c>SignInManagerTest</c> — password check, wrong password,
/// lockout after the configured attempt ceiling, lockout reset on success, and the confirmed-account
/// gate — but against the real <see cref="HuiaSignInManager"/> resolved for a specific
/// <see cref="HuiaAuthFlow"/> and a real Identity + EF Core stack.
/// </summary>
public sealed class HuiaSignInManagerTests : IAsyncLifetime
{
    private const string Tenant = "signinmgr";
    private const string Password = "PasswordOne1!";

    private HuiaTestHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await HuiaTestHost.StartAsync(huia => huia.AddTenant(Tenant, tenant =>
        {
            tenant.Authentication.UseEmailAndPasswordLogin(password =>
            {
                password.RequireConfirmedEmail = true;
                password.MaxFailedAccessAttempts = 3;
                password.LockoutDuration = TimeSpan.FromMinutes(30);
            });
        }));
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Check_password_sign_in_succeeds_with_the_right_password()
    {
        var userId = await _host.SeedUserAsync(Tenant, "right@signinmgr.test", Password, emailConfirmed: true);

        var result = await _host.WithFlowIdentityAsync(Tenant, HuiaAuthFlow.EmailAndPasswordLogin, async fi =>
            await fi.SignInManager.CheckPasswordSignInAsync((await fi.UserManager.FindByIdAsync(userId))!, Password, lockoutOnFailure: false));

        result.ShouldBe(SignInResult.Success);
    }

    [Fact]
    public async Task Check_password_sign_in_fails_with_the_wrong_password()
    {
        var userId = await _host.SeedUserAsync(Tenant, "wrong@signinmgr.test", Password, emailConfirmed: true);

        var result = await _host.WithFlowIdentityAsync(Tenant, HuiaAuthFlow.EmailAndPasswordLogin, async fi =>
            await fi.SignInManager.CheckPasswordSignInAsync((await fi.UserManager.FindByIdAsync(userId))!, "not-the-password", lockoutOnFailure: false));

        result.Succeeded.ShouldBeFalse();
        result.IsLockedOut.ShouldBeFalse();
    }

    [Fact]
    public async Task Check_password_sign_in_locks_the_account_out_after_the_tenant_attempt_ceiling()
    {
        var userId = await _host.SeedUserAsync(Tenant, "lockme@signinmgr.test", Password, emailConfirmed: true);

        var (finalResult, lockedOut) = await _host.WithFlowIdentityAsync(Tenant, HuiaAuthFlow.EmailAndPasswordLogin, async fi =>
        {
            var user = (await fi.UserManager.FindByIdAsync(userId))!;
            SignInResult last = SignInResult.Success;
            // Three wrong attempts reach the ceiling; a fourth is refused up front as LockedOut.
            for (var attempt = 0; attempt < 4; attempt++)
            {
                last = await fi.SignInManager.CheckPasswordSignInAsync(user, "still-wrong", lockoutOnFailure: true);
            }

            return (last, await fi.UserManager.IsLockedOutAsync(user));
        });

        finalResult.IsLockedOut.ShouldBeTrue();
        lockedOut.ShouldBeTrue();
    }

    [Fact]
    public async Task Check_password_sign_in_resets_the_failure_count_on_success()
    {
        var userId = await _host.SeedUserAsync(Tenant, "reset@signinmgr.test", Password, emailConfirmed: true);

        var failureCount = await _host.WithFlowIdentityAsync(Tenant, HuiaAuthFlow.EmailAndPasswordLogin, async fi =>
        {
            var user = (await fi.UserManager.FindByIdAsync(userId))!;
            await fi.SignInManager.CheckPasswordSignInAsync(user, "wrong-once", lockoutOnFailure: true);
            await fi.SignInManager.CheckPasswordSignInAsync(user, Password, lockoutOnFailure: true);
            return await fi.UserManager.GetAccessFailedCountAsync((await fi.UserManager.FindByIdAsync(userId))!);
        });

        failureCount.ShouldBe(0);
    }

    [Fact]
    public async Task Can_sign_in_is_false_for_an_unconfirmed_email_on_the_password_flow()
    {
        var userId = await _host.SeedUserAsync(Tenant, "unconfirmed@signinmgr.test", Password, emailConfirmed: false);

        var canSignIn = await _host.WithFlowIdentityAsync(Tenant, HuiaAuthFlow.EmailAndPasswordLogin, async fi =>
            await fi.SignInManager.CanSignInAsync((await fi.UserManager.FindByIdAsync(userId))!));

        canSignIn.ShouldBeFalse();
    }

    [Fact]
    public async Task A_correct_password_does_not_require_two_factor_for_a_user_without_it()
    {
        var userId = await _host.SeedUserAsync(Tenant, "no2fa@signinmgr.test", Password, emailConfirmed: true);

        var result = await _host.WithFlowIdentityAsync(Tenant, HuiaAuthFlow.EmailAndPasswordLogin, async fi =>
            await fi.SignInManager.CheckPasswordSignInAsync((await fi.UserManager.FindByIdAsync(userId))!, Password, lockoutOnFailure: false));

        result.Succeeded.ShouldBeTrue();
        result.RequiresTwoFactor.ShouldBeFalse();
    }
}
