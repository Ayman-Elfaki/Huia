using System.Net;
using System.Text.Json;
using Huia.AspNetCore.Multitenancy;
using Huia.IntegrationTests.Infrastructure;
using Huia.Options;
using Microsoft.Extensions.DependencyInjection;
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
                tenant.AddScope("reports", scope =>
                {
                    scope.DisplayName = "Reports";
                    scope.Resources.Add("reports-api");
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
}
