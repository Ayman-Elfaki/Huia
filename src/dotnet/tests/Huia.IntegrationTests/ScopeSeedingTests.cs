using System.Net;
using System.Text.Json;
using Huia.IntegrationTests.Infrastructure;
using Huia.Multitenancy;
using Huia.OpenId;
using Huia.OpenId.OpenIddict;
using Huia.OpenId.Options;
using Huia.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using OpenIddict.Abstractions;

namespace Huia.IntegrationTests;

public sealed class ScopeSeedingTests : IAsyncLifetime
{
    private HuiaTestHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await HuiaTestHost.StartAsync(configureOptions: huia =>
        {
            huia.AddTenant("scoped", tenant =>
            {
                tenant.Authentication.UseEmailAndPasswordLogin(password => password.RequireConfirmedEmail = false);
                tenant.AddHuiaOpenId(openId =>
                {
                    openId.AddScope("reports", scope =>
                    {
                        scope.DisplayName = "Reports";
                        scope.Resources.Add("reports-api");
                    });
                });
            });
            huia.AddTenant("unscoped", tenant =>
            {
                tenant.Authentication.UseEmailAndPasswordLogin(password => password.RequireConfirmedEmail = false);
            });
        });

        await _host.SeedMachineClientAsync("scoped", "scoped-worker", "scoped-secret-value", "reports");
        await _host.SeedMachineClientAsync("unscoped", "unscoped-worker", "unscoped-secret-value", "reports");
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task A_declaratively_seeded_scope_can_be_requested_and_lands_in_the_token()
    {
        var response = await RequestTokenAsync("scoped", "scoped-worker", "scoped-secret-value", "reports");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var jwt = new JsonWebToken(body.RootElement.GetProperty("access_token").GetString());
        jwt.Claims.Where(c => c.Type == "scope").Select(c => c.Value).ShouldContain("reports");
    }

    [Fact]
    public async Task A_scope_owned_by_another_tenant_is_not_resolvable()
    {
        var response = await RequestTokenAsync("unscoped", "unscoped-worker", "unscoped-secret-value", "reports");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("error").GetString().ShouldBe("invalid_scope");
    }

    [Fact]
    public async Task Readiness_turns_unhealthy_when_a_seeded_scope_is_deleted()
    {
        (await _host.Client.GetAsync("/health/ready")).StatusCode.ShouldBe(HttpStatusCode.OK);

        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();
            using (HuiaTenantScope.Enter(scope.ServiceProvider, "scoped"))
            {
                var seeded = await manager.FindByNameAsync("reports")
                             ?? throw new InvalidOperationException("The seeded scope was not found.");
                await manager.DeleteAsync(seeded);
            }
        }

        (await _host.Client.GetAsync("/health/ready")).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    private Task<HttpResponseMessage> RequestTokenAsync(string tenant, string clientId, string clientSecret, string scope) =>
        _host.Client.PostAsync($"/{tenant}/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["scope"] = scope,
        }));

    // ------------------------------------------------------------------ pruning

    [Fact]
    public async Task Pruning_deletes_a_static_scope_no_longer_declared_in_its_still_configured_tenant()
    {
        await CreatePhantomStaticScopeAsync("ghost-scope", "scoped");

        await RunScopeSeederAsync(pruneRemovedStaticEntities: true);

        (await ScopeExistsAsync("ghost-scope")).ShouldBeFalse();
    }

    [Fact]
    public async Task Pruning_deletes_a_static_scope_whose_entire_tenant_was_removed_from_config()
    {
        await CreatePhantomStaticScopeAsync("ghost-scope", "ghost-tenant");

        await RunScopeSeederAsync(pruneRemovedStaticEntities: true);

        (await ScopeExistsAsync("ghost-scope")).ShouldBeFalse();
    }

    [Fact]
    public async Task Pruning_is_off_by_default_and_leaves_an_undeclared_static_scope_alone()
    {
        await CreatePhantomStaticScopeAsync("ghost-scope", "scoped");

        await RunScopeSeederAsync(pruneRemovedStaticEntities: false);

        (await ScopeExistsAsync("ghost-scope")).ShouldBeTrue();
    }

    /// <summary>Creates a scope directly through the manager, bypassing the options tree — simulating
    /// "an earlier run declared this, the current code doesn't".</summary>
    private async Task CreatePhantomStaticScopeAsync(string name, string tenantId)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();
        var descriptor = new OpenIddictScopeDescriptor { Name = name };
        descriptor.Properties[HuiaOpenIdConstants.ApplicationProperties.Tenant] = JsonSerializer.SerializeToElement(tenantId);
        descriptor.Properties[HuiaOpenIdConstants.ApplicationProperties.Origin] = JsonSerializer.SerializeToElement(HuiaOpenIdConstants.Origins.Static);
        await manager.CreateAsync(descriptor);
    }

    private async Task<bool> ScopeExistsAsync(string name)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();
        return await manager.FindByNameAsync(name) is not null;
    }

    /// <summary>Re-runs the scope seeder's start-up pass on demand, with the given pruning setting.</summary>
    private async Task RunScopeSeederAsync(bool pruneRemovedStaticEntities)
    {
        _host.Services.GetRequiredService<HuiaOptions>().Seeding.PruneRemovedStaticEntities = pruneRemovedStaticEntities;
        var seeder = _host.Services.GetServices<IHostedService>().OfType<HuiaScopeSeeder>().Single();
        await seeder.StartAsync(CancellationToken.None);
    }
}
