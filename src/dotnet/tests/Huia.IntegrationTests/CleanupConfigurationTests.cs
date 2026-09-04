using System.Net;
using Huia.IntegrationTests.Infrastructure;

namespace Huia.IntegrationTests;

public sealed class CleanupConfigurationTests : IAsyncLifetime
{
    private HuiaTestHost _host = null!;

    public async Task InitializeAsync()
    {
        // Keys stay off (the host's own baseline); Cleanup alone drives whether the centralized
        // AddQuartzHostedService call in HuiaServiceCollectionExtensions actually registers.
        _host = await HuiaTestHost.StartAsync(
            configureOptions: huia => huia.ConfigureCleanup(cleanup => cleanup.EnableBackgroundJobs = true));
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task The_host_starts_and_reports_ready_with_cleanup_background_jobs_enabled()
    {
        var response = await _host.Client.GetAsync("/health/ready");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
