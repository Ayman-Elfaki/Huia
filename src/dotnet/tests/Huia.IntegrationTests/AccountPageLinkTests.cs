using System.Net;
using Huia.IntegrationTests.Infrastructure;
using Huia.Options;

namespace Huia.IntegrationTests;

/// <summary>
/// The dead-end account pages (Error, ConfirmEmail) link to the tenant's client application rather
/// than back to sign-in — the OAuth flow that reached them is spent — and show no link at all when the
/// tenant has no client with a home URL.
/// </summary>
public sealed class AccountPageLinkTests : IAsyncLifetime
{
    private const string Home = "https://link-app.example.test/";

    private HuiaTestHost _host = null!;

    public async Task InitializeAsync() =>
        _host = await HuiaTestHost.StartAsync(configureOptions: huia =>
            huia.AddTenant("linkapp", tenant =>
            {
                tenant.Authentication.UseEmailAndPasswordLogin(password => password.RequireConfirmedEmail = true);
                tenant.AddSinglePageApplication("linkapp-spa", client =>
                {
                    client.RedirectUris.Add(new Uri(Home + "callback"));
                    client.HomeUris.Add(new Uri(Home));
                });
            }));

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task The_error_page_links_to_the_client_home_and_not_to_sign_in()
    {
        var html = await _host.Client.GetStringAsync("/linkapp/identity/account/error");

        html.ShouldContain("data-testid=\"error-home\"");
        html.ShouldContain(Home);
        html.ShouldNotContain("asp-page");            // no razor left unrendered
        html.ShouldNotContain("/identity/account/login");
    }

    [Fact]
    public async Task The_error_page_shows_no_link_when_the_tenant_has_no_client()
    {
        var html = await _host.Client.GetStringAsync("/master/identity/account/error");

        html.ShouldNotContain("data-testid=\"error-home\"");
        html.ShouldNotContain("/identity/account/login");
    }

    [Fact]
    public async Task The_confirm_email_failure_page_links_to_the_client_home()
    {
        // A bogus userId short-circuits before any token decoding: Confirmed = false, page renders.
        var html = await _host.Client.GetStringAsync(
            "/linkapp/identity/account/confirmemail?userId=nobody&code=whatever");

        html.ShouldContain("data-testid=\"confirm-email-home\"");
        html.ShouldContain(Home);
        html.ShouldNotContain("/identity/account/login");
    }
}
