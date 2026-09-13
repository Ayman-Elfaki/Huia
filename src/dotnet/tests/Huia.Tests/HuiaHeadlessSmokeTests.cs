using Huia.Entities;
using Huia.Headless.EntityFrameworkCore;
using Huia.Identity;
using Huia.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.Tests;

/// <summary>
/// Proves the common core surface (AddHuiaHeadless, AddEntityFrameworkCoreStores, the generic managers) is
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
            .AddHuiaHeadless(huia =>
            {
                huia.UseIssuer("https://headless.test");
                huia.UseEmailAndPasswordLogin();
            })
            .AddEntityFrameworkCoreStores<HuiaDbContext>();

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

        // External login shares the flow-store shape with phone login, and is always registered
        // regardless of whether a provider is configured (see AddHuiaHeadless's own comment on why).
        sp.GetRequiredService<Huia.Headless.Services.IExternalLoginFlowStore>().ShouldNotBeNull();
    }

    [Fact]
    public void AddHuiaHeadless_rejects_external_login_with_no_allowed_return_url_prefix()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<HuiaDbContext>(o => o.UseSqlite("DataSource=:memory:"));

        // An unvalidated returnUrl on the challenge would be an open redirect, so this is enforced —
        // not just documented — the moment external login is enabled with no allow-list configured.
        Should.Throw<InvalidOperationException>(() => services
            .AddHuiaHeadless(huia =>
            {
                huia.UseIssuer("https://headless.test");
                huia.UseExternalLogin(ext => ext.AddGoogle("client-id", "client-secret"));
            })
            .AddEntityFrameworkCoreStores<HuiaDbContext>());
    }

    [Fact]
    public void AddHuiaHeadless_registers_a_resolvable_stack_with_external_login_enabled()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<HuiaDbContext>(o => o.UseSqlite("DataSource=:memory:"));

        services
            .AddHuiaHeadless(huia =>
            {
                huia.UseIssuer("https://headless.test");
                huia.UseExternalLogin(ext =>
                {
                    ext.AddGoogle("client-id", "client-secret");
                    ext.AllowReturnUrlPrefix("https://shop.example.com/");
                });
            })
            .AddEntityFrameworkCoreStores<HuiaDbContext>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<Huia.Headless.Services.IExternalLoginFlowStore>().ShouldNotBeNull();
    }
}
