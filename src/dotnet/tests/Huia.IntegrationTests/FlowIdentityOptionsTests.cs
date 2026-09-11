using Huia.Identity;
using Huia.Multitenancy;
using Huia.OpenId.Identity;
using Huia.OpenId.Options;
using Huia.Options;
using Huia.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.IntegrationTests;

/// <summary>
/// The per-flow <see cref="Microsoft.AspNetCore.Identity.IdentityOptions"/> isolation: each
/// <see cref="HuiaAuthFlow"/> gets its own options instance — including its own lockout policy, not one
/// shared across the tenant — plus its own sign-in confirmation rules, so the same account is judged
/// differently depending on the flow it signs in through.
/// </summary>
public sealed class FlowIdentityOptionsTests : IAsyncLifetime
{
    private const string Tenant = "flowtest";

    private HuiaTestHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await HuiaTestHost.StartAsync(huia => huia.AddTenant(Tenant, tenant =>
        {
            tenant.Authentication.UseEmailAndPasswordLogin(password =>
            {
                password.RequireConfirmedEmail = true;
                password.MinimumLength = 11;
                password.MaxFailedAccessAttempts = 7;
            });
            tenant.Authentication.UsePhoneLogin(phone =>
            {
                phone.AllowAutoProvisioning = true;
                phone.MaxFailedAccessAttempts = 4;
            });
            tenant.AddHuiaOpenId(openId =>
            {
                openId.UseExternalLogin(ext => ext.AddOpenIdConnect(
                    "HuiaExternal", "flowtest-client", "flowtest-secret", "https://partner.flowtest.test", p =>
                    {
                        p.DisplayName = "Partner";
                        p.Scopes.Add("email");
                    }));
            });
        }));
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Password_flow_gates_on_a_confirmed_account()
    {
        var signIn = await _host.WithFlowIdentityAsync(Tenant, HuiaAuthFlow.EmailAndPasswordLogin, fi => Task.FromResult(fi.Options.SignIn));

        signIn.RequireConfirmedAccount.ShouldBeTrue();
        signIn.RequireConfirmedPhoneNumber.ShouldBeFalse();
    }

    [Fact]
    public async Task Phone_flow_gates_on_a_confirmed_phone_never_an_email()
    {
        var signIn = await _host.WithFlowIdentityAsync(Tenant, HuiaAuthFlow.PhoneLogin, fi => Task.FromResult(fi.Options.SignIn));

        signIn.RequireConfirmedPhoneNumber.ShouldBeTrue();
        signIn.RequireConfirmedEmail.ShouldBeFalse();
        signIn.RequireConfirmedAccount.ShouldBeFalse();
    }

    [Fact]
    public async Task External_flow_gates_on_neither()
    {
        var signIn = await _host.WithFlowIdentityAsync(Tenant, HuiaAuthFlow.ExternalLogin, fi => Task.FromResult(fi.Options.SignIn));

        signIn.RequireConfirmedAccount.ShouldBeFalse();
        signIn.RequireConfirmedEmail.ShouldBeFalse();
        signIn.RequireConfirmedPhoneNumber.ShouldBeFalse();
    }

    [Fact]
    public void Each_flow_has_its_own_options_instance()
    {
        using var scope = _host.Services.CreateScope();
        using var _ = HuiaTenantScope.Enter(scope.ServiceProvider, Tenant);
        var factory = scope.ServiceProvider.GetRequiredService<IHuiaFlowIdentityFactory>();

        var password = factory.Create(HuiaAuthFlow.EmailAndPasswordLogin).Options;
        var phone = factory.Create(HuiaAuthFlow.PhoneLogin).Options;
        var external = factory.Create(HuiaAuthFlow.ExternalLogin).Options;

        ReferenceEquals(password, phone).ShouldBeFalse();
        ReferenceEquals(password, external).ShouldBeFalse();
        ReferenceEquals(phone, external).ShouldBeFalse();
    }

    [Fact]
    public async Task The_password_flow_carries_its_own_password_and_lockout_policy()
    {
        var options = await _host.WithFlowIdentityAsync(Tenant, HuiaAuthFlow.EmailAndPasswordLogin, fi => Task.FromResult(fi.Options));

        options.Password.RequiredLength.ShouldBe(11);
        options.Lockout.MaxFailedAccessAttempts.ShouldBe(7);
    }

    [Fact]
    public async Task The_phone_flow_carries_a_lockout_policy_independent_of_the_password_flow()
    {
        var options = await _host.WithFlowIdentityAsync(Tenant, HuiaAuthFlow.PhoneLogin, fi => Task.FromResult(fi.Options));

        // A different ceiling than the password flow (7) — proves lockout is per flow, not per tenant.
        options.Lockout.MaxFailedAccessAttempts.ShouldBe(4);
    }

    [Fact]
    public async Task The_default_flow_carries_no_tenant_lockout_policy()
    {
        var options = await _host.WithFlowIdentityAsync(Tenant, HuiaAuthFlow.Default, fi => Task.FromResult(fi.Options));

        // Neither flow's ceiling (7 or 4) leaks onto the flow-agnostic default — it stays at the
        // ASP.NET Core Identity stock default, since nothing on that flow ever checks lockout.
        options.Lockout.MaxFailedAccessAttempts.ShouldBe(5);
    }

    [Fact]
    public async Task An_unconfirmed_email_account_can_sign_in_through_the_phone_flow_but_not_the_password_flow()
    {
        var userId = await _host.SeedUserAsync(
            Tenant, "half-baked@flowtest.test", "PasswordOne1!", emailConfirmed: false, phoneNumber: "+15005559090");

        var canPassword = await _host.WithFlowIdentityAsync(Tenant, HuiaAuthFlow.EmailAndPasswordLogin, async fi =>
            await fi.SignInManager.CanSignInAsync((await fi.UserManager.FindByIdAsync(userId))!));
        var canPhone = await _host.WithFlowIdentityAsync(Tenant, HuiaAuthFlow.PhoneLogin, async fi =>
            await fi.SignInManager.CanSignInAsync((await fi.UserManager.FindByIdAsync(userId))!));

        canPassword.ShouldBeFalse();
        canPhone.ShouldBeTrue();
    }

    [Fact]
    public async Task Confirming_the_email_lets_the_password_flow_sign_in()
    {
        var userId = await _host.SeedUserAsync(
            Tenant, "will-confirm@flowtest.test", "PasswordOne1!", emailConfirmed: true);

        var canPassword = await _host.WithFlowIdentityAsync(Tenant, HuiaAuthFlow.EmailAndPasswordLogin, async fi =>
            await fi.SignInManager.CanSignInAsync((await fi.UserManager.FindByIdAsync(userId))!));

        canPassword.ShouldBeTrue();
    }
}
