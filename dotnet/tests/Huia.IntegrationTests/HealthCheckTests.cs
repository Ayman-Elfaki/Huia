using System.Net;
using Huia.IntegrationTests.Infrastructure;
using Huia.Options;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;

namespace Huia.IntegrationTests;

public sealed class HealthCheckTests : IAsyncLifetime
{
    private HuiaTestHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await HuiaTestHost.StartAsync(configureOptions: huia =>
        {
            huia.AddTenant("healthy", tenant =>
            {
                tenant.Authentication.Password.RequireConfirmedEmail = false;
                tenant.Authentication.UsePasswordFlow();
                tenant.AddMachineToMachineApplication("healthy-worker", "healthy-secret-value");
            });
        });
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task The_liveness_endpoint_reports_healthy()
    {
        var response = await _host.Client.GetAsync("/health/live");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("Healthy");
    }

    [Fact]
    public async Task The_readiness_endpoint_reports_healthy_once_seeding_has_run()
    {
        var response = await _host.Client.GetAsync("/health/ready");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("Healthy");
    }

    [Fact]
    public async Task The_readiness_endpoint_reports_unhealthy_when_a_configured_client_is_missing()
    {
        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            var application = await manager.FindByClientIdAsync("healthy-worker")
                              ?? throw new InvalidOperationException("The seeded client was not found.");
            await manager.DeleteAsync(application);
        }

        var response = await _host.Client.GetAsync("/health/ready");

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync()).ShouldBe("Unhealthy");
    }
}
