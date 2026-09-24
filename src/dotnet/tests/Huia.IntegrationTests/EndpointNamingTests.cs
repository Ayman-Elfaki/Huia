using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Huia.Entities;
using Huia.Headless.Endpoints;
using Huia.Headless.EntityFrameworkCore;
using Huia.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using HuiaDbContext = Huia.Headless.EntityFrameworkCore.HuiaDbContext<Huia.Entities.HuiaUser, Huia.Entities.HuiaRole, string>;

namespace Huia.IntegrationTests;

public sealed class EndpointNamingTests : IAsyncLifetime
{
    private HuiaTestHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await HuiaTestHost.StartAsync(configureEndpoints: endpoints => endpoints.MapHuiaHome());
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public void OpenId_endpoints_have_standardized_names()
    {
        var endpointDataSources = _host.Services.GetServices<EndpointDataSource>();
        var endpoints = endpointDataSources.SelectMany(ds => ds.Endpoints).ToList();

        var namedEndpoints = endpoints
            .Select(ep => ep.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName)
            .Where(name => !string.IsNullOrEmpty(name))
            .ToHashSet();

        // Verify core OpenID Connect endpoints
        Assert.Contains("huia.connect.authorize", namedEndpoints);
        Assert.Contains("huia.connect.token", namedEndpoints);
        Assert.Contains("huia.connect.userinfo", namedEndpoints);
        Assert.Contains("huia.connect.logout", namedEndpoints);

        // Verify Admin API endpoints
        Assert.Contains("huia.admin.tenants.list", namedEndpoints);
        Assert.Contains("huia.admin.users.list", namedEndpoints);
        Assert.Contains("huia.admin.users.get", namedEndpoints);
        Assert.Contains("huia.admin.users.create", namedEndpoints);
        Assert.Contains("huia.admin.users.update", namedEndpoints);
        Assert.Contains("huia.admin.users.delete", namedEndpoints);
        Assert.Contains("huia.admin.roles.list", namedEndpoints);
        Assert.Contains("huia.admin.clients.list", namedEndpoints);
        Assert.Contains("huia.admin.keys.list", namedEndpoints);
        Assert.Contains("huia.admin.scopes.list", namedEndpoints);

        // Verify Manage API endpoints
        Assert.Contains("huia.manage.profile.get", namedEndpoints);
        Assert.Contains("huia.manage.profile.update", namedEndpoints);
        Assert.Contains("huia.manage.email.get", namedEndpoints);
        Assert.Contains("huia.manage.email.update", namedEndpoints);
        Assert.Contains("huia.manage.password.update", namedEndpoints);
        Assert.Contains("huia.manage.phone.get", namedEndpoints);
        Assert.Contains("huia.manage.external-logins.list", namedEndpoints);

        // Verify Passkey endpoints
        Assert.Contains("huia.passkey.assertion", namedEndpoints);
        Assert.Contains("huia.passkey.assertion-options", namedEndpoints);
        Assert.Contains("huia.passkey.creation-options", namedEndpoints);
        Assert.Contains("huia.passkey.list", namedEndpoints);
        Assert.Contains("huia.passkey.register", namedEndpoints);
        Assert.Contains("huia.passkey.rename", namedEndpoints);
        Assert.Contains("huia.passkey.remove", namedEndpoints);

        // Verify External auth endpoints
        Assert.Contains("huia.external.challenge", namedEndpoints);
        Assert.Contains("huia.external.callback", namedEndpoints);
        Assert.Contains("huia.external.dispatch", namedEndpoints);
        Assert.Contains("huia.external.signout-callback", namedEndpoints);

        // Verify Health & Root endpoints
        Assert.Contains("huia.health.live", namedEndpoints);
        Assert.Contains("huia.health.ready", namedEndpoints);
        Assert.Contains("huia.root", namedEndpoints);
    }

    [Fact]
    public async Task Headless_endpoints_have_standardized_names()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var builder = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton(connection);
                    services.AddDbContext<HuiaDbContext>(o => o.UseSqlite(connection));

                    services
                        .AddHuiaHeadless(huia =>
                        {
                            huia.UseIssuer("https://headless-test.test");
                            huia.UseEmailAndPasswordLogin();
                            huia.UsePhoneLogin(p => { });
                            huia.UsePasskeyLogin();
                            huia.UseExternalLogin(ext =>
                            {
                                ext.AddGoogle("client", "secret");
                                ext.AllowReturnUrlPrefix("https://shop.example.com/");
                            });
                        })
                        .AddEntityFrameworkCoreStores<HuiaDbContext>();
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapHuiaHeadlessEndpoints();
                        endpoints.MapHuiaHeadlessAdminEndpoints();
                    });
                });
            });

        var host = await builder.StartAsync();

        var endpointDataSources = host.Services.GetServices<EndpointDataSource>();
        var endpoints = endpointDataSources.SelectMany(ds => ds.Endpoints).ToList();

        var namedEndpoints = endpoints
            .Select(ep => ep.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName)
            .Where(name => !string.IsNullOrEmpty(name))
            .ToHashSet();

        Assert.Contains("huia.headless.register", namedEndpoints);
        Assert.Contains("huia.headless.me", namedEndpoints);
        Assert.Contains("huia.headless.phone.start", namedEndpoints);
        Assert.Contains("huia.headless.phone.verify", namedEndpoints);
        Assert.Contains("huia.headless.phone.complete-profile", namedEndpoints);
        Assert.Contains("huia.headless.passkey.assertion", namedEndpoints);
        Assert.Contains("huia.headless.passkey.assertion-options", namedEndpoints);
        Assert.Contains("huia.headless.passkey.creation-options", namedEndpoints);
        Assert.Contains("huia.headless.passkey.list", namedEndpoints);
        Assert.Contains("huia.headless.passkey.register", namedEndpoints);
        Assert.Contains("huia.headless.passkey.rename", namedEndpoints);
        Assert.Contains("huia.headless.passkey.remove", namedEndpoints);
        Assert.Contains("huia.headless.external.challenge", namedEndpoints);
        Assert.Contains("huia.headless.external.callback", namedEndpoints);
        Assert.Contains("huia.headless.external.exchange", namedEndpoints);
        Assert.Contains("huia.headless.external.complete-profile", namedEndpoints);
        Assert.Contains("huia.headless.admin.users.list", namedEndpoints);
        Assert.Contains("huia.headless.admin.users.get", namedEndpoints);
        Assert.Contains("huia.headless.admin.users.create", namedEndpoints);
        Assert.Contains("huia.headless.admin.users.update", namedEndpoints);
        Assert.Contains("huia.headless.admin.users.delete", namedEndpoints);
        Assert.Contains("huia.headless.admin.roles.list", namedEndpoints);
        Assert.Contains("huia.headless.admin.roles.get", namedEndpoints);
        Assert.Contains("huia.headless.admin.roles.create", namedEndpoints);
        Assert.Contains("huia.headless.admin.roles.update", namedEndpoints);
        Assert.Contains("huia.headless.admin.roles.delete", namedEndpoints);

        await host.StopAsync();
        host.Dispose();
        await connection.DisposeAsync();
    }
}
