using System.Net;
using Huia.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Builder;

namespace Huia.IntegrationTests;

/// <summary>
/// Covers <c>MapHuiaHome</c>: the bare root redirects to a tenant, but a tenant root must never redirect
/// back to itself (the cause of the sign-out redirect loop).
/// </summary>
public sealed class HomeEndpointTests : IAsyncLifetime
{
    private HuiaTestHost _host = null!;

    public async Task InitializeAsync() =>
        _host = await HuiaTestHost.StartAsync(configureEndpoints: endpoints => endpoints.MapHuiaHome("master"));

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task The_bare_root_redirects_to_the_fallback_tenant()
    {
        var response = await _host.Client.GetAsync("/");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().ShouldBe("/master/");
    }

    [Fact]
    public async Task An_anonymous_hit_on_a_tenant_root_goes_to_sign_in()
    {
        var response = await _host.Client.GetAsync("/phone/");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().ShouldContain("/identity/account/login");
    }

    [Fact]
    public async Task A_tenant_root_never_redirects_to_itself()
    {
        var response = await _host.Client.GetAsync("/phone/");

        response.Headers.Location?.ToString().ShouldNotBe("/phone/");
        response.Headers.Location?.ToString().ShouldNotBe("/phone");
    }
}
