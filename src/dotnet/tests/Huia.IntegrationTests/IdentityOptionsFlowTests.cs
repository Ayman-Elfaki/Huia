using Huia.AspNetCore.Identity;
using Huia.AspNetCore.Multitenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Huia.IntegrationTests.Infrastructure;

namespace Huia.IntegrationTests;

/// <summary>
/// Mirrors ASP.NET Core's <c>IdentityOptionsTest</c> at the wiring level: the named per-flow
/// <see cref="IdentityOptions"/> instances registered by <c>AddHuiaFlowIdentity</c> each carry their
/// own password-or-none, lockout, and <c>SignIn</c> confirmation policy — nothing is shared across
/// flows any more — while the default (empty-name) instance carries the tenant's password policy but
/// no lockout and keeps every <c>SignIn</c> gate off.
/// </summary>
public sealed class IdentityOptionsFlowTests : IAsyncLifetime
{
    private const string Tenant = "optsflow";

    private HuiaTestHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await HuiaTestHost.StartAsync(huia => huia.AddTenant(Tenant, tenant =>
        {
            tenant.Authentication.UseEmailAndPasswordLogin(password =>
            {
                password.RequireConfirmedEmail = true;
                password.MinimumLength = 13;
                password.MaxFailedAccessAttempts = 4;
            });
            tenant.Authentication.UsePhoneLogin(phone =>
            {
                phone.AllowAutoProvisioning = true;
                phone.MaxFailedAccessAttempts = 6;
            });
        }));
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public void The_password_flow_options_carry_the_tenant_password_and_lockout_policy()
    {
        var (password, _, _) = Resolve();

        password.Password.RequiredLength.ShouldBe(13);
        password.Lockout.MaxFailedAccessAttempts.ShouldBe(4);
    }

    [Fact]
    public void The_phone_flow_options_carry_their_own_lockout_policy()
    {
        var (_, phone, _) = Resolve();

        // Distinct from the password flow's ceiling (4) — each flow's lockout is independent.
        phone.Lockout.MaxFailedAccessAttempts.ShouldBe(6);
    }

    [Fact]
    public void The_password_flow_options_gate_on_a_confirmed_account()
    {
        var (password, _, _) = Resolve();

        password.SignIn.RequireConfirmedAccount.ShouldBeTrue();
        password.SignIn.RequireConfirmedPhoneNumber.ShouldBeFalse();
    }

    [Fact]
    public void The_phone_flow_options_gate_on_a_confirmed_phone_number()
    {
        var (_, phone, _) = Resolve();

        phone.SignIn.RequireConfirmedPhoneNumber.ShouldBeTrue();
        phone.SignIn.RequireConfirmedAccount.ShouldBeFalse();
        phone.SignIn.RequireConfirmedEmail.ShouldBeFalse();
    }

    [Fact]
    public void The_default_options_carry_the_tenant_password_policy_but_no_lockout()
    {
        var (_, _, fallback) = Resolve();

        fallback.Password.RequiredLength.ShouldBe(13);

        // Neither flow's ceiling (4 or 6) leaks onto the default instance.
        fallback.Lockout.MaxFailedAccessAttempts.ShouldBe(5);
    }

    [Fact]
    public void The_default_options_keep_every_sign_in_gate_off()
    {
        var (_, _, fallback) = Resolve();

        fallback.SignIn.RequireConfirmedEmail.ShouldBeFalse();
        fallback.SignIn.RequireConfirmedAccount.ShouldBeFalse();
        fallback.SignIn.RequireConfirmedPhoneNumber.ShouldBeFalse();
    }

    [Fact]
    public void Every_flow_including_the_default_carries_the_default_token_providers()
    {
        var (password, phone, fallback) = Resolve();

        foreach (var options in new[] { password, phone, fallback })
        {
            options.Tokens.ProviderMap.ShouldContainKey(TokenOptions.DefaultProvider);
            options.Tokens.ProviderMap.ShouldContainKey(TokenOptions.DefaultEmailProvider);
        }
    }

    private (IdentityOptions Password, IdentityOptions Phone, IdentityOptions Fallback) Resolve()
    {
        using var scope = _host.Services.CreateScope();
        using var _ = HuiaTenantScope.Enter(scope.ServiceProvider, Tenant);
        var snapshot = scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<IdentityOptions>>();

        return (
            snapshot.Get(HuiaFlowIdentityOptions.EmailAndPassword),
            snapshot.Get(HuiaFlowIdentityOptions.PhoneLogin),
            snapshot.Get(Microsoft.Extensions.Options.Options.DefaultName));
    }
}
