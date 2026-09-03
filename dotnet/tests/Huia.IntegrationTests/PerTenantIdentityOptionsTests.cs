using Huia.AspNetCore.Multitenancy;
using Huia.EntityFrameworkCore.Entities;
using Huia.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Huia.IntegrationTests;

/// <summary>
/// Per-tenant <see cref="IdentityOptions"/>: each tenant's password / lockout / uniqueness policy is
/// projected onto the options the Identity managers read, including through the singleton
/// <c>IOptions&lt;IdentityOptions&gt;</c> that <c>AddHuiaPerTenantIdentityOptions</c> bridges to the
/// tenant-aware snapshot.
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
                tenant.Authentication.Password.RequireConfirmedEmail = false;
                tenant.Authentication.UsePasswordFlow(password =>
                {
                    password.MinimumLength = 14;
                    password.RequireNonAlphanumeric = true;
                });
                tenant.Lockout.MaxFailedAccessAttempts = 3;
                tenant.Lockout.LockoutDuration = TimeSpan.FromHours(1);
            });

            huia.AddTenant("lax", tenant =>
            {
                tenant.Authentication.Password.RequireConfirmedEmail = false;
                tenant.Authentication.UsePasswordFlow(password =>
                {
                    password.MinimumLength = 6;
                    password.RequireDigit = false;
                    password.RequireUppercase = false;
                    password.RequireLowercase = false;
                });
                tenant.Lockout.MaxFailedAccessAttempts = 10;
            });
        });
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Theory]
    [InlineData("strict", 14, true, 3)]
    [InlineData("lax", 6, false, 10)]
    public void The_options_snapshot_carries_the_tenant_policy(
        string tenantId, int expectedLength, bool expectedRequireDigit, int expectedMaxAttempts)
    {
        using var scope = _host.Services.CreateScope();
        using var _ = HuiaTenantScope.Enter(scope.ServiceProvider, tenantId);

        var options = scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<IdentityOptions>>().Value;

        options.Password.RequiredLength.ShouldBe(expectedLength);
        options.Password.RequireDigit.ShouldBe(expectedRequireDigit);
        options.Lockout.MaxFailedAccessAttempts.ShouldBe(expectedMaxAttempts);
    }

    [Theory]
    [InlineData("strict", 14, 3)]
    [InlineData("lax", 6, 10)]
    public void The_UserManager_sees_the_tenant_policy(string tenantId, int expectedLength, int expectedMaxAttempts)
    {
        using var scope = _host.Services.CreateScope();
        using var _ = HuiaTenantScope.Enter(scope.ServiceProvider, tenantId);

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<HuiaUser>>();

        userManager.Options.Password.RequiredLength.ShouldBe(expectedLength);
        userManager.Options.Lockout.MaxFailedAccessAttempts.ShouldBe(expectedMaxAttempts);
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
