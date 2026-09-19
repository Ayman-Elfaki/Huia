using Finbuckle.MultiTenant.Abstractions;
using Huia.Entities;
using Huia.Headless.EntityFrameworkCore;
using Huia.OpenId.EntityFrameworkCore.Multitenancy;
using OpenIdHuiaRole = Huia.OpenId.EntityFrameworkCore.Entities.HuiaRole;
using OpenIdHuiaUser = Huia.OpenId.EntityFrameworkCore.Entities.HuiaUser;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.Tests.Persistence;

public sealed class CustomHuiaDbContextTests
{
    [Fact]
    public void AddEntityFrameworkCoreStores_registers_a_custom_context_when_options_are_missing()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services
            .AddHuiaHeadless(huia =>
            {
                huia.UseIssuer("https://headless.test");
                huia.UseEmailAndPasswordLogin();
            })
            .AddEntityFrameworkCoreStores<CustomHeadlessDbContext>();

        services.ShouldContain(descriptor => descriptor.ServiceType == typeof(DbContextOptions<CustomHeadlessDbContext>));
        services.ShouldContain(descriptor => descriptor.ServiceType == typeof(CustomHeadlessDbContext));
    }

    [Fact]
    public void A_custom_headless_context_uses_the_Huia_schema()
    {
        var options = new DbContextOptionsBuilder<CustomHeadlessDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        using var context = new CustomHeadlessDbContext(options);

        context.Model.FindEntityType(typeof(HuiaUser))!.GetTableName().ShouldBe("HuiaUsers");
        context.Model.FindEntityType(typeof(HuiaRole))!.GetTableName().ShouldBe("HuiaRoles");
    }

    [Fact]
    public void A_custom_openid_context_keeps_the_tenant_and_OpenIddict_model()
    {
        var options = new DbContextOptionsBuilder<CustomOpenIdDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        var accessor = new StaticMultiTenantContextAccessor<HuiaTenantInfo>(null);

        using var context = new CustomOpenIdDbContext(accessor, options);

        context.Model.FindEntityType(typeof(OpenIdHuiaUser))!.GetTableName().ShouldBe("HuiaUsers");
        context.Model.GetEntityTypes()
            .ShouldContain(entity => entity.ClrType.Name == "OpenIddictEntityFrameworkCoreApplication");
    }

    public sealed class CustomHeadlessDbContext(DbContextOptions<CustomHeadlessDbContext> options)
        : Huia.Headless.EntityFrameworkCore.HuiaDbContext<HuiaUser, HuiaRole, string>(options)
    {
    }

    public sealed class CustomOpenIdDbContext(
        IMultiTenantContextAccessor accessor,
        DbContextOptions<CustomOpenIdDbContext> options)
        : Huia.OpenId.EntityFrameworkCore.HuiaDbContext<OpenIdHuiaUser, OpenIdHuiaRole, string>(accessor, options)
    {
    }
}