using Huia.DependencyInjection;
using Huia.Headless.EntityFrameworkCore;
using Huia.OpenId;
using Huia.OpenId.EntityFrameworkCore;
using Huia.Options;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Huia.IntegrationTests;

public sealed class MutualExclusivityTests
{
    [Fact]
    public void AddHuiaOpenId_followed_by_AddHuiaHeadless_throws_InvalidOperationException()
    {
        var services = new ServiceCollection();
        services.AddHuia(h => { h.UseIssuer("https://localhost"); h.AddTenant("default", t => t.Authentication.UseEmailAndPasswordLogin()); })
            .AddHuiaOpenId();

        var ex = Should.Throw<InvalidOperationException>(() =>
        {
            services.AddHuia(h => { h.UseIssuer("https://localhost"); h.AddTenant("default", t => t.Authentication.UseEmailAndPasswordLogin()); })
                .AddHuiaHeadless();
        });

        ex.Message.ShouldContain("AddHuiaHeadless() cannot be combined with AddHuiaOpenId()");
    }

    [Fact]
    public void AddHuiaHeadless_followed_by_AddHuiaOpenId_throws_InvalidOperationException()
    {
        var services = new ServiceCollection();
        services.AddHuia(h => { h.UseIssuer("https://localhost"); h.AddTenant("default", t => t.Authentication.UseEmailAndPasswordLogin()); })
            .AddHuiaHeadless();

        var ex = Should.Throw<InvalidOperationException>(() =>
        {
            services.AddHuia(h => { h.UseIssuer("https://localhost"); h.AddTenant("default", t => t.Authentication.UseEmailAndPasswordLogin()); })
                .AddHuiaOpenId();
        });

        ex.Message.ShouldContain("AddHuiaOpenId() cannot be combined with AddHuiaHeadless()");
    }

    [Fact]
    public void Tenant_AddHuiaOpenId_then_AddHuiaHeadless_throws_HuiaOptionsException()
    {
        var tenant = new TenantOptions();
        tenant.AddHuiaOpenId(_ => { });

        var ex = Should.Throw<HuiaOptionsException>(() =>
        {
            tenant.AddHuiaHeadless(_ => { });
        });

        ex.Message.ShouldContain("AddHuiaHeadless() cannot be configured on a tenant that already has OpenId options configured");
    }

    [Fact]
    public void Tenant_AddHuiaHeadless_then_AddHuiaOpenId_throws_HuiaOptionsException()
    {
        var tenant = new TenantOptions();
        tenant.AddHuiaHeadless(_ => { });

        var ex = Should.Throw<HuiaOptionsException>(() =>
        {
            tenant.AddHuiaOpenId(_ => { });
        });

        ex.Message.ShouldContain("AddHuiaOpenId() cannot be configured on a tenant that already has Headless options configured");
    }

    [Fact]
    public void Registering_HuiaOpenIdDbContext_then_calling_AddHuiaHeadless_throws_InvalidOperationException()
    {
        var services = new ServiceCollection();
        using var connection = new SqliteConnection("DataSource=:memory:");
        services.AddDbContext<HuiaOpenIdDbContext>(o => o.UseSqlite(connection));

        var ex = Should.Throw<InvalidOperationException>(() =>
        {
            services.AddHuia(h => { h.UseIssuer("https://localhost"); h.AddTenant("default", t => t.Authentication.UseEmailAndPasswordLogin()); })
                .AddHuiaHeadless();
        });

        ex.Message.ShouldContain("HuiaHeadlessDbContext cannot be combined with HuiaOpenIdDbContext");
    }

    [Fact]
    public void Registering_HuiaHeadlessDbContext_then_calling_AddHuiaOpenId_throws_InvalidOperationException()
    {
        var services = new ServiceCollection();
        using var connection = new SqliteConnection("DataSource=:memory:");
        services.AddDbContext<HuiaHeadlessDbContext>(o => o.UseSqlite(connection));

        var ex = Should.Throw<InvalidOperationException>(() =>
        {
            services.AddHuia(h => { h.UseIssuer("https://localhost"); h.AddTenant("default", t => t.Authentication.UseEmailAndPasswordLogin()); })
                .AddHuiaOpenId();
        });

        ex.Message.ShouldContain("HuiaOpenIdDbContext cannot be combined with HuiaHeadlessDbContext");
    }
}
