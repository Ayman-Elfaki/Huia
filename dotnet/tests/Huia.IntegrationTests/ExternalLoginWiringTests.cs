using System.Net;
using Huia.IntegrationTests.Infrastructure;

namespace Huia.IntegrationTests;

public sealed class ExternalLoginWiringTests : IAsyncLifetime
{
    private HuiaTestHost _host = null!;

    public async Task InitializeAsync() => _host = await HuiaTestHost.StartAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task The_login_page_renders_a_button_for_each_configured_provider()
    {
        var html = await _host.Client.GetStringAsync("/consumer/identity/account/login");

        html.ShouldContain("external-providers");
        html.ShouldContain("/consumer/identity/account/external/HuiaExternal");
        html.ShouldContain("Partner");
    }

    [Fact]
    public async Task The_challenge_endpoint_requires_antiforgery()
    {
        var response = await _host.Client.PostAsync(
            "/consumer/identity/account/external/HuiaExternal",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["returnUrl"] = "/" }));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_unknown_provider_is_not_found()
    {
        var client = _host.CreateClient();
        var page = await client.GetStringAsync("/consumer/identity/account/login");
        var token = System.Text.RegularExpressions.Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;

        var response = await client.PostAsync(
            "/consumer/identity/account/external/Nope",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["returnUrl"] = "/",
                ["__RequestVerificationToken"] = token,
            }));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_signin_callback_route_is_mapped()
    {
        // No upstream state -> the callback authenticate fails and we bounce to the login page.
        var response = await _host.Client.GetAsync("/consumer/signin-huiaexternal");
        response.StatusCode.ShouldBeOneOf(HttpStatusCode.Redirect, HttpStatusCode.OK, HttpStatusCode.BadRequest);
    }
}
