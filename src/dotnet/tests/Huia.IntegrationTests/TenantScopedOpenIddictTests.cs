using System.Net;
using Huia.Multitenancy;
using Huia.IntegrationTests.Infrastructure;
using Huia.Options;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;

namespace Huia.IntegrationTests;

/// <summary>
/// OpenIddict clients and scopes carry a real <c>TenantId</c> column: the same client id / scope name may
/// exist in two tenants, and each tenant only ever sees (and authenticates) its own.
/// </summary>
public sealed class TenantScopedOpenIddictTests : IAsyncLifetime
{
    private HuiaTestHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await HuiaTestHost.StartAsync(configureOptions: huia =>
        {
            foreach (var (tenantId, secret) in new[] { ("alpha", "alpha-secret-value"), ("beta", "beta-secret-value") })
            {
                huia.AddTenant(tenantId, tenant =>
                {
                    tenant.Authentication.UseEmailAndPasswordLogin(p => p.RequireConfirmedEmail = false);
                    tenant.AddMachineToMachineApplication("shared-worker", secret);
                    tenant.AddScope("api:read", scope => scope.DisplayName = $"{tenantId} read");
                });
            }
        });
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task The_same_client_id_in_two_tenants_authenticates_only_with_its_own_secret()
    {
        (await RequestTokenAsync("alpha", "alpha-secret-value")).ShouldBe(HttpStatusCode.OK);
        (await RequestTokenAsync("beta", "beta-secret-value")).ShouldBe(HttpStatusCode.OK);

        (await RequestTokenAsync("alpha", "beta-secret-value")).ShouldNotBe(HttpStatusCode.OK);
        (await RequestTokenAsync("beta", "alpha-secret-value")).ShouldNotBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Lookups_inside_a_tenant_scope_only_return_that_tenants_client_and_scope()
    {
        foreach (var tenantId in new[] { "alpha", "beta" })
        {
            await using var scope = _host.Services.CreateAsyncScope();
            using (scope.ServiceProvider.GetRequiredService<IHuiaTenantContext>()
                       .EnterTenantScope(scope.ServiceProvider, tenantId))
            {
                var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
                var scopes = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();

                var application = await applications.FindByClientIdAsync("shared-worker");
                application.ShouldNotBeNull();
                var properties = await applications.GetPropertiesAsync(application);
                properties["huia:tenant"].GetString().ShouldBe(tenantId);

                var apiScope = await scopes.FindByNameAsync("api:read");
                apiScope.ShouldNotBeNull();
                (await scopes.GetDisplayNameAsync(apiScope)).ShouldBe($"{tenantId} read");
            }
        }
    }

    private async Task<HttpStatusCode> RequestTokenAsync(string tenant, string secret)
    {
        var response = await _host.Client.PostAsync($"/{tenant}/connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = "shared-worker",
                ["client_secret"] = secret,
            }));
        return response.StatusCode;
    }
}
