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
        Assert.Contains(HuiaConstants.Endpoints.Connect.Authorize, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Connect.Token, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Connect.UserInfo, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Connect.Logout, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Connect.Verify, namedEndpoints);

        // Verify Admin API endpoints
        Assert.Contains(HuiaConstants.Endpoints.Admin.Tenants.List, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Admin.Users.List, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Admin.Users.Get, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Admin.Users.Create, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Admin.Users.Update, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Admin.Users.Delete, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Admin.Users.Claims.List, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Admin.Users.Claims.Add, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Admin.Users.Claims.Remove, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Admin.Users.Claims.RemoveByQuery, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Admin.Users.Roles.List, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Admin.Users.Roles.Add, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Admin.Users.Roles.Remove, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Admin.Users.Lock, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Admin.Users.Unlock, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Admin.Users.VerifyEmail, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Admin.Roles.List, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Admin.Roles.Get, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Admin.Roles.Create, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Admin.Roles.Update, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Admin.Roles.Delete, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Admin.Clients.List, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Admin.Clients.Get, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Admin.Clients.Create, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Admin.Clients.Update, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Admin.Clients.Delete, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Admin.Keys.List, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Admin.Keys.Get, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Admin.Keys.Create, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Admin.Keys.Revoke, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Admin.Keys.Delete, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Admin.Scopes.List, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Admin.Scopes.Create, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Admin.Scopes.Update, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Admin.Scopes.Delete, namedEndpoints);

        // Verify Manage API endpoints
        Assert.Contains(HuiaConstants.Endpoints.Manage.Profile.Get, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Manage.Profile.Update, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Manage.Email.Get, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Manage.Email.Update, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Manage.Email.Confirm, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Manage.Password.Update, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Manage.Phone.Get, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Manage.Phone.Update, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Manage.Phone.Confirm, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Manage.Phone.Delete, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Manage.ExternalLogins.List, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Manage.ExternalLogins.Delete, namedEndpoints);

        // Verify Passkey endpoints
        Assert.Contains(HuiaConstants.Endpoints.Passkey.Assertion, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Passkey.AssertionOptions, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Passkey.CreationOptions, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Passkey.List, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Passkey.Register, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Passkey.Rename, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Passkey.Remove, namedEndpoints);

        // Verify External auth endpoints
        Assert.Contains(HuiaConstants.Endpoints.External.Challenge, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.External.Callback, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.External.Dispatch, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.External.SignOutCallback, namedEndpoints);

        // Verify Health & Root endpoints
        Assert.Contains(HuiaConstants.Endpoints.Health.Live, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Health.Ready, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Root, namedEndpoints);
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

        Assert.Contains(HuiaConstants.Endpoints.Headless.Register, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Headless.Me, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Headless.Phone.Start, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Headless.Phone.Verify, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Headless.Phone.CompleteProfile, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Headless.Passkey.Assertion, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Headless.Passkey.AssertionOptions, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Headless.Passkey.CreationOptions, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Headless.Passkey.List, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Headless.Passkey.Register, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Headless.Passkey.Rename, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Headless.Passkey.Remove, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Headless.External.Challenge, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Headless.External.Callback, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Headless.External.Exchange, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Headless.External.CompleteProfile, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Headless.Admin.Users.List, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Headless.Admin.Users.Get, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Headless.Admin.Users.Create, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Headless.Admin.Users.Update, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Headless.Admin.Users.Delete, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Headless.Admin.Users.Lock, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Headless.Admin.Users.Unlock, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Headless.Admin.Users.Roles.List, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Headless.Admin.Users.Roles.Add, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Headless.Admin.Users.Roles.Remove, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Headless.Admin.Users.Claims.List, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Headless.Admin.Users.Claims.Add, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Headless.Admin.Users.Claims.Remove, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Headless.Admin.Users.Claims.RemoveByQuery, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Headless.Admin.Roles.List, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Headless.Admin.Roles.Get, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Headless.Admin.Roles.Create, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Headless.Admin.Roles.Update, namedEndpoints);
        Assert.Contains(HuiaConstants.Endpoints.Headless.Admin.Roles.Delete, namedEndpoints);

        await host.StopAsync();
        host.Dispose();
        await connection.DisposeAsync();
    }
}
