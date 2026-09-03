using System.Net;
using System.Net.Http.Json;
using Huia.IntegrationTests.Infrastructure;

namespace Huia.IntegrationTests;

public sealed class MultiTenancyPipelineTests : IAsyncLifetime
{
    private HuiaTestHost _host = null!;

    public async Task InitializeAsync() => _host = await HuiaTestHost.StartAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Theory]
    [InlineData("acme")]
    [InlineData("master")]
    public async Task A_request_under_a_known_tenant_segment_resolves_that_tenant_and_rebases_the_path(string tenant)
    {
        var result = await _host.Client.GetFromJsonAsync<HuiaTestHost.ProbeResult>($"/{tenant}/probe");

        result.ShouldNotBeNull();
        result!.Tenant.ShouldBe(tenant);
        result.PathBase.ShouldBe($"/{tenant}");
        result.Path.ShouldBe("/probe");
    }

    [Fact]
    public async Task A_request_under_an_unknown_tenant_segment_is_not_routed()
    {
        var response = await _host.Client.GetAsync("/does-not-exist/probe");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Tenants_are_isolated_from_one_another_on_the_pipeline()
    {
        var acme = await _host.Client.GetFromJsonAsync<HuiaTestHost.ProbeResult>("/acme/probe");
        var master = await _host.Client.GetFromJsonAsync<HuiaTestHost.ProbeResult>("/master/probe");

        acme!.Tenant.ShouldBe("acme");
        master!.Tenant.ShouldBe("master");
    }
}
