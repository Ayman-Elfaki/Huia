using Huia.AspNetCore.Multitenancy;
using Huia.EntityFrameworkCore.Entities;
using Huia.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Huia.IntegrationTests;

/// <summary>
/// Per-tenant <see cref="IdentityOptions"/> on the <c>Default</c> flow: each tenant's password /
/// uniqueness policy is projected onto the options the DI-injected Identity managers read (used by
/// <c>/manage</c>, <c>/admin</c>, seeding), including through the singleton
/// <c>IOptions&lt;IdentityOptions&gt;</c> that <c>AddHuiaPerTenantIdentityOptions</c> bridges to the
/// tenant-aware snapshot. Lockout is no longer part of this projection — it is per flow now (see
/// <c>FlowIdentityOptionsTests</c> / <c>IdentityOptionsFlowTests</c>), and the <c>Default</c> flow is
/// never consulted by a sign-in check, so it carries none.
/// </summary>
public sealed class PerTenantIdentityOptionsTests : IAsyncLifetime
{
    private HuiaTestHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await HuiaTestHost.StartAsync(huia =>
        {
            huia.AddTenant("strict", tenant =>
            {
                tenant.Authentication.UseEmailAndPasswordLogin(password =>
                {
                    password.RequireConfirmedEmail = false;
                    password.MinimumLength = 14;
                    password.RequireNonAlphanumeric = true;
                });
            });

            huia.AddTenant("lax", tenant =>
            {
                tenant.Authentication.UseEmailAndPasswordLogin(password =>
                {
                    password.RequireConfirmedEmail = false;
                    password.MinimumLength = 6;
                    password.RequireDigit = false;
                    password.RequireUppercase = false;
                    password.RequireLowercase = false;
                });
            });
        });
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Theory]
    [InlineData("strict", 14, true)]
    [InlineData("lax", 6, false)]
    public void The_options_snapshot_carries_the_tenant_password_policy(
        string tenantId, int expectedLength, bool expectedRequireDigit)
    {
        using var scope = _host.Services.CreateScope();
        using var _ = HuiaTenantScope.Enter(scope.ServiceProvider, tenantId);

        var options = scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<IdentityOptions>>().Value;

        options.Password.RequiredLength.ShouldBe(expectedLength);
        options.Password.RequireDigit.ShouldBe(expectedRequireDigit);
    }

    [Theory]
    [InlineData("strict", 14)]
    [InlineData("lax", 6)]
    public void The_UserManager_sees_the_tenant_password_policy(string tenantId, int expectedLength)
    {
        using var scope = _host.Services.CreateScope();
        using var _ = HuiaTenantScope.Enter(scope.ServiceProvider, tenantId);

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<HuiaUser>>();

        userManager.Options.Password.RequiredLength.ShouldBe(expectedLength);
    }

    [Theory]
    [InlineData("strict")]
    [InlineData("lax")]
    public void The_default_options_do_not_gate_sign_in_on_confirmation(string tenantId)
    {
        using var scope = _host.Services.CreateScope();
        using var _ = HuiaTenantScope.Enter(scope.ServiceProvider, tenantId);

        var options = scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<IdentityOptions>>().Value;

        // Confirmation gating lives on the named per-flow options now, never on the default instance.
        options.SignIn.RequireConfirmedEmail.ShouldBeFalse();
        options.SignIn.RequireConfirmedAccount.ShouldBeFalse();
        options.SignIn.RequireConfirmedPhoneNumber.ShouldBeFalse();
    }

    [Fact]
    public async Task A_password_below_the_tenant_minimum_is_rejected()
    {
        // 12 chars, mixed case + digit + symbol: fine under "lax", too short under "strict" (min 14).
        var shortForStrict = () => _host.SeedUserAsync("strict", "sam@strict.test", "Abcd1234!xyz@");
        await Should.ThrowAsync<InvalidOperationException>(shortForStrict);

        // 6 chars, letters only: allowed under "lax" (min 6, no complexity).
        await _host.SeedUserAsync("lax", "sam@lax.test", "abcdef");
    }
}
