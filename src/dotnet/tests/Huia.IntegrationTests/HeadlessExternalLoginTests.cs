using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Huia.Headless.EntityFrameworkCore;
using Huia.Headless.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Huia.IntegrationTests;

/// <summary>
/// Covers <c>Huia.Headless</c>'s external-login endpoints. The challenge endpoint's <c>returnUrl</c>
/// allow-list is exercised directly against the real handler (an unvalidated value here would be an
/// open redirect, since — unlike <c>Huia.OpenId</c>'s same-origin case — the browser ends up on a
/// *different* origin than Huia itself). The dispatch/exchange/complete-profile round trip is exercised
/// by signing directly into <see cref="IdentityConstants.ExternalScheme"/> through a test-only endpoint
/// that stands in for a real provider's remote-authentication handler (which does exactly this once it
/// resolves a principal) — this tests Huia's own link-or-create logic without depending on a live
/// Google/GitHub/etc. account.
/// </summary>
public sealed class HeadlessExternalLoginTests
{
    [Fact]
    public async Task Challenge_redirects_to_the_real_provider_when_the_return_url_is_allow_listed()
    {
        await using var host = await StartAsync();

        var response = await host.Client.GetAsync(
            "identity/account/external/google?returnUrl=" + Uri.EscapeDataString("https://shop.example.com/auth/callback"));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().ShouldStartWith("https://accounts.google.com/");
    }

    [Fact]
    public async Task Challenge_rejects_a_return_url_outside_the_allow_list()
    {
        await using var host = await StartAsync();

        var response = await host.Client.GetAsync(
            "identity/account/external/google?returnUrl=" + Uri.EscapeDataString("https://evil.example.com/"));

        response.IsSuccessStatusCode.ShouldBeFalse();
        response.StatusCode.ShouldNotBe(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Challenge_404s_for_an_unregistered_provider()
    {
        await using var host = await StartAsync();

        var response = await host.Client.GetAsync(
            "identity/account/external/not-a-real-provider?returnUrl=" + Uri.EscapeDataString("https://shop.example.com/"));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Exchange_and_complete_profile_reject_an_unknown_code()
    {
        await using var host = await StartAsync();

        var exchange = await host.Client.PostAsJsonAsync("identity/account/external/exchange", new { code = "does-not-exist" });
        exchange.IsSuccessStatusCode.ShouldBeFalse();

        var complete = await host.Client.PostAsJsonAsync("identity/account/external/complete-profile",
            new { code = "does-not-exist", firstName = "A", lastName = "B" });
        complete.IsSuccessStatusCode.ShouldBeFalse();
    }

    [Fact]
    public async Task A_first_time_sign_in_dispatches_to_a_new_sign_up_that_completes_with_a_bearer_token()
    {
        await using var host = await StartAsync();
        const string returnUrl = "https://shop.example.com/auth/callback";

        var signIn = await host.Client.PostAsJsonAsync("test/external-signin", new
        {
            scheme = "google",
            subject = "google-subject-1",
            email = "nina@ext.test",
            name = "Nina New",
            returnUrl,
        });
        signIn.EnsureSuccessStatusCode();

        var dispatch = await host.Client.GetAsync("identity/account/external/callback");
        dispatch.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var location = dispatch.Headers.Location!;
        location.ToString().ShouldStartWith(returnUrl);
        var code = QueryHelpers.ParseQuery(location.Query)["code"].ToString();
        code.ShouldNotBeNullOrEmpty();

        var exchange = await host.Client.PostAsJsonAsync("identity/account/external/exchange", new { code });
        exchange.EnsureSuccessStatusCode();
        var exchangeBody = await exchange.Content.ReadFromJsonAsync<JsonElement>();
        exchangeBody.GetProperty("requiresProfile").GetBoolean().ShouldBeTrue();
        exchangeBody.GetProperty("firstName").GetString().ShouldBe("Nina");
        exchangeBody.GetProperty("lastName").GetString().ShouldBe("New");

        var complete = await host.Client.PostAsJsonAsync("identity/account/external/complete-profile",
            new { code, firstName = "Nina", lastName = "New" });
        complete.EnsureSuccessStatusCode();
        var tokens = await complete.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = tokens.GetProperty("accessToken").GetString();
        accessToken.ShouldNotBeNullOrEmpty();

        var me = new HttpRequestMessage(HttpMethod.Get, "identity/me");
        me.Headers.Add("Authorization", $"Bearer {accessToken}");
        var meResponse = await host.Client.SendAsync(me);
        meResponse.EnsureSuccessStatusCode();
        var meBody = await meResponse.Content.ReadFromJsonAsync<JsonElement>();
        meBody.GetProperty("email").GetString().ShouldBe("nina@ext.test");
        meBody.GetProperty("firstName").GetString().ShouldBe("Nina");
    }

    [Fact]
    public async Task A_returning_linked_account_signs_in_directly_without_a_profile_step()
    {
        await using var host = await StartAsync();
        const string returnUrl = "https://shop.example.com/auth/callback";

        await host.SeedExternalUserAsync("google", "google-subject-2", "gary@ext.test", "Gary", "Green");

        var signIn = await host.Client.PostAsJsonAsync("test/external-signin", new
        {
            scheme = "google",
            subject = "google-subject-2",
            email = "gary@ext.test",
            name = "Gary Green",
            returnUrl,
        });
        signIn.EnsureSuccessStatusCode();

        var dispatch = await host.Client.GetAsync("identity/account/external/callback");
        dispatch.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var code = QueryHelpers.ParseQuery(dispatch.Headers.Location!.Query)["code"].ToString();

        // Already linked — exchange returns the bearer token body directly, not {requiresProfile: true}.
        var exchange = await host.Client.PostAsJsonAsync("identity/account/external/exchange", new { code });
        exchange.EnsureSuccessStatusCode();
        var body = await exchange.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("accessToken").GetString().ShouldNotBeNullOrEmpty();
    }

    private static async Task<HeadlessTestHost> StartAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var builder = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton(connection);
                    services.AddDbContext<HuiaDbContext>(o => o.UseSqlite(connection));

                    services
                        .AddHuiaHeadless(huia =>
                        {
                            huia.UseIssuer("https://headless-external.test");
                            huia.UseExternalLogin(ext =>
                            {
                                ext.AddGoogle("test-client-id", "test-client-secret");
                                ext.AllowReturnUrlPrefix("https://shop.example.com/");
                            });
                        })
                        .AddEntityFrameworkCoreStores<HuiaDbContext>();
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapHuiaHeadlessEndpoints();

                        // Stands in for a real provider's remote-authentication handler: signs a
                        // resolved principal into IdentityConstants.ExternalScheme exactly as the
                        // Google/GitHub/etc. handler does on a successful callback, so the dispatch
                        // endpoint's own link-or-create logic can be tested without a live provider.
                        endpoints.MapPost("test/external-signin", async (HttpContext ctx, TestExternalSignIn body) =>
                        {
                            var identity = new ClaimsIdentity(body.Scheme);
                            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, body.Subject));
                            if (body.Email is not null)
                            {
                                identity.AddClaim(new Claim(ClaimTypes.Email, body.Email));
                            }

                            if (body.Name is not null)
                            {
                                identity.AddClaim(new Claim(ClaimTypes.Name, body.Name));
                            }

                            var properties = new AuthenticationProperties();
                            // A real challenge sets this (see ChallengeAsync) and it survives the
                            // round trip through the provider automatically; this test double stands in
                            // for that survival by setting it directly.
                            properties.Items["LoginProvider"] = body.Scheme;
                            properties.Items["huia:return"] = body.ReturnUrl;
                            await ctx.SignInAsync(IdentityConstants.ExternalScheme, new ClaimsPrincipal(identity), properties);
                            return Results.Ok();
                        });
                    });
                });
            });

        var host = await builder.StartAsync();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<HuiaDbContext>().Database.EnsureCreatedAsync();
        }

        return new HeadlessTestHost(host, connection);
    }

    private sealed record TestExternalSignIn(string Scheme, string Subject, string? Email, string? Name, string ReturnUrl);

    private sealed class HeadlessTestHost(IHost host, SqliteConnection connection) : IAsyncDisposable
    {
        public HttpClient Client { get; } = CreateClient(host);

        public async Task SeedExternalUserAsync(string loginProvider, string providerKey, string email, string firstName, string lastName)
        {
            await using var scope = host.Services.CreateAsyncScope();
            var userManager = scope.ServiceProvider.GetRequiredService<HuiaUserManager>();
            var (result, _) = await userManager.CreateExternalUserAsync(
                "ext", email, firstName, lastName, loginProvider, providerKey, loginProvider);
            result.Succeeded.ShouldBeTrue(string.Join(" ", result.Errors.Select(e => e.Description)));
        }

        private static HttpClient CreateClient(IHost host)
        {
            var server = host.GetTestServer();
            var handler = new CookieForwardingHandler(new CookieContainer()) { InnerHandler = server.CreateHandler() };
            // https, not server.BaseAddress (http) — the external-scheme cookie is hardened to
            // Secure+SameSite=None (it must survive a real cross-site redirect through the provider in
            // production), and CookieContainer correctly refuses to resend a Secure cookie over http.
            // TestServer needs no real TLS handshake for this; it only reads the request URI's scheme.
            return new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await host.StopAsync();
            host.Dispose();
            await connection.DisposeAsync();
        }
    }

    /// <summary>Carries Set-Cookie responses back as Cookie headers on the next request — TestServer's own client does not.</summary>
    private sealed class CookieForwardingHandler(CookieContainer cookies) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var header = cookies.GetCookieHeader(uri);
            if (!string.IsNullOrEmpty(header))
            {
                request.Headers.Add("Cookie", header);
            }

            var response = await base.SendAsync(request, cancellationToken);

            if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
            {
                foreach (var setCookie in setCookies)
                {
                    cookies.SetCookies(uri, setCookie);
                }
            }

            return response;
        }
    }
}
