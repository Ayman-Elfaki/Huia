using Huia.Entities;
using Huia.Headless.EntityFrameworkCore;
using Huia.Identity;
using Huia.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.Tests;

/// <summary>
/// Proves the common core surface (AddHuia, AddEntityFrameworkCoreStores, the generic managers) is
/// actually consumable by a second, structurally different flavor — not just OpenId reflected back at
/// itself. <see cref="ServiceProviderOptions.ValidateOnBuild"/> also catches captive-dependency mistakes
/// (as it did for <c>HuiaRoleSeeder</c> during development) before anything tries to boot for real.
/// </summary>
public sealed class HuiaHeadlessSmokeTests
{
    [Fact]
    public void AddHuiaHeadless_registers_a_resolvable_Identity_stack()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<HuiaDbContext>(o => o.UseSqlite("DataSource=:memory:"));

        services
            .AddHuia(huia =>
            {
                huia.UseIssuer("https://headless.test");
                huia.AddTenant("default", tenant => tenant.Authentication.UseEmailAndPasswordLogin());
            })
            .AddEntityFrameworkCoreStores<HuiaDbContext>()
            .AddHuiaHeadless();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        sp.GetRequiredService<UserManager<HuiaUser>>().ShouldBeOfType<Huia.Headless.Identity.HuiaUserManager>();
        sp.GetRequiredService<SignInManager<HuiaUser>>().ShouldBeOfType<HuiaSignInManager<HuiaUser>>();
        sp.GetRequiredService<RoleManager<HuiaRole>>().ShouldNotBeNull();
        sp.GetRequiredService<HuiaPasskeyRegistrar<HuiaUser>>().ShouldNotBeNull();

        // Passwordless SMS phone login shares its services with Huia.OpenId.
        sp.GetRequiredService<IPhoneNumberService>().ShouldNotBeNull();
        sp.GetRequiredService<IOtpService<HuiaUser>>().ShouldNotBeNull();
        sp.GetRequiredService<IOtpRateLimiter>().ShouldNotBeNull();
        sp.GetRequiredService<IPhoneLoginRateLimiter>().ShouldNotBeNull();
        sp.GetRequiredService<IPendingPhoneSignup>().ShouldNotBeNull();
        sp.GetRequiredService<ISmsSender>().ShouldNotBeNull();
        sp.GetRequiredService<Huia.Headless.Services.IPhoneLoginFlowStore>().ShouldNotBeNull();
    }

    [Fact]
    public void AddHuiaHeadless_rejects_anything_other_than_exactly_one_tenant()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<HuiaDbContext>(o => o.UseSqlite("DataSource=:memory:"));

        var builder = services
            .AddHuia(huia =>
            {
                huia.UseIssuer("https://headless.test");
                huia.AddTenant("one", tenant => tenant.Authentication.UseEmailAndPasswordLogin());
                huia.AddTenant("two", tenant => tenant.Authentication.UseEmailAndPasswordLogin());
            })
            .AddEntityFrameworkCoreStores<HuiaDbContext>();

        Should.Throw<InvalidOperationException>(() => builder.AddHuiaHeadless());
    }
}
