using System.Net;
using Huia.IntegrationTests.Infrastructure;

namespace Huia.IntegrationTests;

public sealed class StatusPageTests : IAsyncLifetime
{
    private HuiaTestHost _host = null!;

    public async Task InitializeAsync() => _host = await HuiaTestHost.StartAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task An_unmatched_route_renders_the_branded_404_page()
    {
        var response = await _host.Client.GetAsync("/master/does-not-exist");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");

        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("data-testid=\"status-page\"");
        body.ShouldContain("data-status-code=\"404\"");
        body.ShouldContain("find the page you were looking for");
    }

    [Fact]
    public async Task An_api_path_gets_the_bare_status_code_with_no_html()
    {
        var response = await _host.Client.GetAsync("/master/admin/users");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotContain("data-testid=\"status-page\"");
    }
}
