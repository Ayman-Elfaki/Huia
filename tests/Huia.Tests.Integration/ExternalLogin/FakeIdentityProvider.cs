using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Huia.Tests.Integration.ExternalLogin;

/// <summary>
/// A minimal, in-process OAuth2 provider — <c>/authorize</c>, <c>/token</c>, <c>/userinfo</c> — hosted on its
/// own <see cref="TestServer"/>, standing in for a real external IdP (Google, etc.) in tests. Its own
/// <see cref="CreateClient"/> simulates the browser visiting it (the <c>/authorize</c> redirect a real user
/// would follow); <see cref="CreateBackchannelHandler"/> is what the app under test's <c>AddOAuth</c>
/// registration uses for its server-to-server token/userinfo calls (see
/// <c>ExternalLoginTestFactory</c>) — both routes into the same in-memory server, so no real network call
/// ever happens.
/// </summary>
public sealed class FakeIdentityProvider : IDisposable
{
    private readonly IHost _host;
    private readonly TestServer _server;

    /// <summary>The identity <c>/userinfo</c> returns for the next completed flow — settable per test.</summary>
    public FakeExternalIdentity NextIdentity { get; set; } = new("fake-subject-1", "user@example.com",
        "Test", "User", EmailVerified: true);

    /// <summary>When set, <c>/authorize</c> redirects back with this error instead of a code — simulates the
    /// user cancelling, or the provider denying, the request.</summary>
    public string? SimulatedAuthorizeError { get; set; }

    public FakeIdentityProvider()
    {
        _host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services => services.AddRouting());
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/authorize", HandleAuthorize);
                        endpoints.MapPost("/token", HandleToken);
                        endpoints.MapGet("/userinfo", HandleUserInfo);
                    });
                });
            })
            .Start();

        _server = _host.GetTestServer();
    }

    /// <summary>A client whose requests land on this fake IdP — use to simulate the browser following the
    /// redirect to <c>/authorize</c>.</summary>
    public HttpClient CreateClient() => _server.CreateClient();

    /// <summary>The handler <c>OAuthOptions.BackchannelHttpHandler</c> should use, so the app under test's
    /// own server-to-server token/userinfo calls land here instead of attempting a real network call.</summary>
    public HttpMessageHandler CreateBackchannelHandler() => _server.CreateHandler();

    private IResult HandleAuthorize(HttpRequest request)
    {
        var redirectUri = request.Query["redirect_uri"].ToString();
        var state = request.Query["state"].ToString();

        var location = SimulatedAuthorizeError is not null
            ? $"{redirectUri}?error={Uri.EscapeDataString(SimulatedAuthorizeError)}&state={Uri.EscapeDataString(state)}"
            : $"{redirectUri}?code=fake-authorization-code&state={Uri.EscapeDataString(state)}";

        return Results.Redirect(location);
    }

    private static IResult HandleToken() => Results.Json(new
    {
        access_token = "fake-access-token",
        token_type = "Bearer",
        expires_in = 3600,
    });

    private IResult HandleUserInfo(HttpRequest request)
    {
        if (request.Headers.Authorization != "Bearer fake-access-token")
        {
            return Results.Unauthorized();
        }

        var identity = NextIdentity;

        return Results.Json(new
        {
            sub = identity.Subject,
            email = identity.Email,
            given_name = identity.GivenName,
            family_name = identity.FamilyName,
            email_verified = identity.EmailVerified ? "true" : "false",
            picture = identity.Picture,
        });
    }

    public void Dispose()
    {
        _host.Dispose();
    }
}
