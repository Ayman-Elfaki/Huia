using Huia.Multitenancy;
using Huia.IntegrationTests.Infrastructure;
using Huia.Options;
using Huia.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.IntegrationTests;

/// <summary>The EF-backed tenant store and the provider that serves persisted tenants beside code-first ones.</summary>
public sealed class PersistedTenantsTests : IAsyncLifetime
{
    private HuiaTestHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await HuiaTestHost.StartAsync(configureBuilder: builder => builder.AddPersistedTenants());
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private static HuiaTenant NewTenant()
    {
        var tenant = new HuiaTenant { DisplayName = "Initech" };
        tenant.Branding.AccentColor = "#112233";
        tenant.Authentication.UseEmailAndPasswordLogin(p =>
        {
            p.MinimumLength = 12;
            p.RequireConfirmedEmail = true;
        });
        tenant.Authentication.UsePhoneLogin(p => p.DefaultCountry = "SA");
        tenant.Roles.Add("support");
        tenant.AddScope("api:read", s => s.DisplayName = "Read");
        tenant.AddMachineToMachineApplication("initech-worker", "worker-secret-value",
            app => app.Token.AccessToken = TimeSpan.FromMinutes(7));
        tenant.AddServerSideWebApplication("initech-web", "web-secret-value", app =>
            app.RedirectUris.Add(new Uri("https://initech.test/callback")));
        return tenant;
    }

    [Fact]
    public async Task A_saved_tenant_round_trips_through_the_protected_store()
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IHuiaTenantStore>();

        var saved = await store.SaveAsync("initech", NewTenant());
        saved.Version.ShouldBe(1);

        var loaded = await store.FindAsync("initech");
        loaded.ShouldNotBeNull();
        var tenant = loaded.Tenant;

        tenant.Identifier.ShouldBe("initech");
        tenant.DisplayName.ShouldBe("Initech");
        tenant.Branding.AccentColor.ShouldBe("#112233");
        tenant.Authentication.EmailAndPassword!.MinimumLength.ShouldBe(12);
        tenant.Authentication.EmailAndPassword.RequireConfirmedEmail.ShouldBeTrue();
        tenant.Authentication.Phone!.DefaultCountry.ShouldBe("SA");
        tenant.Roles.ShouldBe(["support"]);
        tenant.Scopes.Single().Name.ShouldBe("api:read");
        tenant.Applications.Count.ShouldBe(2);

        var worker = tenant.Applications.OfType<MachineToMachineApplication>().Single();
        worker.ClientId.ShouldBe("initech-worker");
        worker.ClientSecret.ShouldBe("worker-secret-value");
        worker.Token.AccessToken.ShouldBe(TimeSpan.FromMinutes(7));

        var web = tenant.Applications.OfType<ServerSideWebApplication>().Single();
        web.RedirectUris.Single().ToString().ShouldBe("https://initech.test/callback");
    }

    [Fact]
    public async Task Saving_again_replaces_the_tenant_and_bumps_the_version()
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IHuiaTenantStore>();

        await store.SaveAsync("initech", NewTenant());
        var renamed = NewTenant();
        renamed.DisplayName = "Initech Two";
        var second = await store.SaveAsync("initech", renamed);

        second.Version.ShouldBe(2);
        (await store.FindAsync("initech"))!.Tenant.DisplayName.ShouldBe("Initech Two");
        (await store.ListAsync()).Count.ShouldBe(1);
    }

    [Fact]
    public async Task An_invalid_tenant_is_rejected_and_not_stored()
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IHuiaTenantStore>();

        var invalid = NewTenant();
        invalid.Roles.Add("has space");

        await Should.ThrowAsync<HuiaOptionsException>(() => store.SaveAsync("bad", invalid));
        (await store.FindAsync("bad")).ShouldBeNull();
    }

    [Fact]
    public async Task Deleting_removes_the_tenant()
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IHuiaTenantStore>();

        await store.SaveAsync("initech", NewTenant());
        (await store.DeleteAsync("initech")).ShouldBeTrue();
        (await store.DeleteAsync("initech")).ShouldBeFalse();
        (await store.FindAsync("initech")).ShouldBeNull();
    }

    [Fact]
    public async Task The_provider_serves_a_persisted_tenant_after_a_refresh_and_code_first_tenants_win()
    {
        var provider = _host.Services.GetRequiredService<IHuiaTenantProvider>();
        provider.ContainsKey("initech").ShouldBeFalse();
        provider.ContainsKey("master").ShouldBeTrue();

        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IHuiaTenantStore>();
            await store.SaveAsync("initech", NewTenant());

            // Same id as a code-first tenant: the code-first definition must keep winning.
            var shadow = new HuiaTenant { DisplayName = "Not the real master" };
            shadow.Authentication.UseEmailAndPasswordLogin();
            await store.SaveAsync("master", shadow);
        }

        await _host.Services.GetRequiredService<IHuiaTenantRefresher>().RefreshAsync();

        provider.TryGetValue("initech", out var initech).ShouldBeTrue();
        initech!.DisplayName.ShouldBe("Initech");
        provider["master"].DisplayName.ShouldNotBe("Not the real master");
    }
}
