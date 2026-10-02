using System.Net;
using Huia.Identity;
using Huia.Options;
using Huia.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Client;
using OpenIddict.Client.AspNetCore;
using Shouldly;
using Xunit;
using HuiaUser = Huia.OpenId.EntityFrameworkCore.Entities.HuiaUser;
using HuiaUserManager = Huia.Identity.HuiaUserManager<Huia.OpenId.EntityFrameworkCore.Entities.HuiaUser>;

namespace Huia.IntegrationTests;

public sealed class OpenIdExternalLoginTests
{
    [Fact]
    public async Task Google_provider_is_registered_in_openiddict_client()
    {
        await using var host = await StartHostAsync();

        var clientOptions = host.Services.GetRequiredService<IOptionsMonitor<OpenIddictClientOptions>>().CurrentValue;
        var googleReg = clientOptions.Registrations.FirstOrDefault(r => r.RegistrationId == "todo:Google");

        googleReg.ShouldNotBeNull();
        googleReg.ClientId.ShouldBe("test-google-client-id");
        googleReg.RedirectUri.ShouldBe(new Uri("signin-google", UriKind.Relative));
    }

    [Fact]
    public async Task Can_get_server_configuration_for_google()
    {
        await using var host = await StartHostAsync();
        var clientService = host.Services.GetRequiredService<OpenIddictClientService>();
        var config = await clientService.GetServerConfigurationByRegistrationIdAsync("todo:Google");
        config.ShouldNotBeNull();
    }

    [Fact]
    public async Task Challenge_google_returns_redirect_to_google()
    {
        await using var host = await StartHostAsync();
        var client = host.CreateClient();
        var page = await client.GetStringAsync("/todo/identity/account/login");
        var token = System.Text.RegularExpressions.Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;

        var response = await client.PostAsync(
            "/todo/identity/account/external/Google",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["returnUrl"] = "/",
                ["__RequestVerificationToken"] = token,
            }));

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect, $"Response body was: {body}");
        var location = response.Headers.Location;
        location.ShouldNotBeNull();
        location.ToString().ShouldStartWith("https://accounts.google.com/o/oauth2/v2/auth");
        location.ToString().ShouldContain("client_id=test-google-client-id");
        location.ToString().ShouldContain("scope=openid");
    }

    [Fact]
    public async Task Logout_with_google_external_idp_succeeds_without_crashing()
    {
        await using var host = await StartHostAsync(configureEndpoints: endpoints =>
        {
            endpoints.MapGet("/fake-google-login", async (HttpContext context) =>
            {
                try
                {
                    var userManager = context.RequestServices.GetRequiredService<UserManager<HuiaUser>>();
                    var signInManager = context.RequestServices.GetRequiredService<SignInManager<HuiaUser>>();
                    var user = await userManager.FindByEmailAsync("googler@example.com");
                    var claims = new List<System.Security.Claims.Claim>
                    {
                        new(HuiaConstants.ClaimTypes.ExternalIdp, "todo:Google"),
                    };
                    await signInManager.SignInWithClaimsAsync(user!, isPersistent: false, claims);
                    return Results.Ok();
                }
                catch (Exception ex)
                {
                    return Results.Problem(ex.ToString());
                }
            });
        });

        await host.SeedUserAsync("todo", "googler@example.com", null);

        var client = host.CreateClient();
        var loginResp = await client.GetAsync("/todo/fake-google-login");
        var loginBody = await loginResp.Content.ReadAsStringAsync();
        loginResp.StatusCode.ShouldBe(HttpStatusCode.OK, loginBody);

        var response = await client.GetAsync("/todo/connect/logout?client_id=todo-app&post_logout_redirect_uri=https%3A%2F%2Flocalhost%3A3000%2F");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect, $"Response ({response.StatusCode}) body was: {body}");
        response.Headers.Location!.ToString().ShouldBe("https://localhost:3000/");
    }

    [Fact]
    public async Task Google_authority_is_included_in_csp_form_action()
    {
        await using var host = await StartHostAsync();

        var response = await host.Client.GetAsync("/todo/identity/account/login");
        response.Headers.TryGetValues("Content-Security-Policy", out var cspValues).ShouldBeTrue();
        var csp = string.Join("; ", cspValues);
        csp.ShouldContain("https://accounts.google.com");
    }

    [Fact]
    public async Task The_login_page_renders_google_button()
    {
        await using var host = await StartHostAsync();
        var html = await host.Client.GetStringAsync("/todo/identity/account/login");

        html.ShouldContain("external-providers");
        html.ShouldContain("/todo/identity/account/external/Google");
        html.ShouldContain("Google");
        html.ShouldContain("huia-provider-google");
    }

    private static Task<HuiaTestHost> StartHostAsync(Action<IEndpointRouteBuilder>? configureEndpoints = null) =>
        HuiaTestHost.StartAsync(
            configureOptions: huia =>
            {
                huia.AddTenant("todo", tenant =>
                {
                    tenant.Authentication.UseEmailAndPasswordLogin(p => p.RequireConfirmedEmail = false);
                    tenant.Authentication.UseExternalLogin(ext =>
                    {
                        ext.AddGoogle("test-google-client-id", "test-google-secret", g =>
                        {
                            g.Scopes.Add("email");
                            g.Scopes.Add("profile");
                        });
                    });
                    tenant.AddServerSideWebApplication("todo-app", "todo-app-secret", client =>
                    {
                        client.RedirectUris.Add(new Uri("https://localhost:3000/auth/oidc/callback"));
                        client.PostLogoutRedirectUris.Add(new Uri("https://localhost:3000/"));
                        client.HomeUris.Add(new Uri("https://localhost:3000/"));
                    });
                });
            },
            configureEndpoints: configureEndpoints,
            configureBuilder: b => b.AddHuiaSecurityHeaders());
}
