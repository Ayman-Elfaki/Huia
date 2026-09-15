using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.E2ETests;

/// <summary>
/// Boots the real <c>Huia.AppHost</c> (Postgres + Mailpit + the identity server, with the E2E surface
/// on and an ephemeral database) through <c>Aspire.Hosting.Testing</c>. Used by the email-flow specs,
/// which drive the identity server's own Razor pages and read the resulting message back from Mailpit's
/// REST API. Any start-up failure (no Docker, no DCP, ports taken) leaves <see cref="Started"/> false and
/// the specs skip.
/// </summary>
public sealed class AppHostFixture : IAsyncLifetime
{
    private DistributedApplication? _app;

    public bool Started { get; private set; }
    public string? SkipReason { get; private set; }
    public string Issuer { get; private set; } = "";
    public string ExternalIssuer { get; private set; } = "";
    public string ShopApiUrl { get; private set; } = "";
    public string AdminAppUrl { get; private set; } = "";
    public string TodoAppUrl { get; private set; } = "";
    public string ShopAppUrl { get; private set; } = "";
    public string TodoNextUrl { get; private set; } = "";
    public string ShopNextUrl { get; private set; } = "";
    public MailpitClient Mailpit { get; private set; } = null!;

    /// <summary>An <see cref="HttpClient"/> pointed at the identity server, accepting its dev certificate.</summary>
    public HttpClient CreateIdpClient(System.Net.CookieContainer? cookies = null)
        => CreateClient(Issuer, cookies);

    /// <summary>
    /// An <see cref="HttpClient"/> pointed at any AppHost endpoint, accepting its dev certificate.
    /// <paramref name="baseUri"/> is typically one of this fixture's *.dev.localhost URLs — connects via
    /// <see cref="Loopback"/> since a raw HttpClient uses the OS resolver, unlike Playwright/the browser
    /// (which has Chromium's built-in "*.localhost is loopback" handling and should keep navigating the
    /// original *.dev.localhost URLs directly — that's what makes the app's own request-derived
    /// redirect_uri match what AppHost.cs registers for it).
    /// </summary>
    public HttpClient CreateClient(string baseUri, System.Net.CookieContainer? cookies = null)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            UseCookies = true,
            CookieContainer = cookies ?? new System.Net.CookieContainer(),
            AllowAutoRedirect = false,
        };
        return new HttpClient(handler) { BaseAddress = new Uri(Loopback(baseUri) + "/") };
    }

    /// <summary>
    /// The plain-loopback form of a *.dev.localhost AppHost URL (same host, same port) — for consumers
    /// that go through the OS resolver instead of Chromium's built-in "*.localhost is loopback" handling.
    /// At least in this environment the OS resolver fails to resolve *.dev.localhost outright (real DNS
    /// lookup, no hosts-file entry), while plain "localhost" always works and reaches the exact same
    /// unproxied, fixed-port endpoint (see AppHost.cs).
    /// </summary>
    private static string Loopback(string url) => new UriBuilder(url) { Host = "localhost" }.Uri.ToString().TrimEnd('/');

    public async Task InitializeAsync()
    {
        try
        {
            // Args are parsed into the AppHost's configuration before its body runs — setting them on
            // builder.Configuration afterwards is too late (resources are already declared).
            var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Huia_AppHost>(
                ["--Huia:EnableE2E=true", "--Huia:UsePostgresVolume=false"]);

            _app = await builder.BuildAsync();
            await _app.StartAsync().WaitAsync(TimeSpan.FromMinutes(6));

            var notifications = _app.Services.GetRequiredService<ResourceNotificationService>();
            await notifications.WaitForResourceAsync("mailpit", KnownResourceStates.Running)
                .WaitAsync(TimeSpan.FromMinutes(2));
            await notifications.WaitForResourceHealthyAsync("huia-external")
                .WaitAsync(TimeSpan.FromMinutes(3));
            await notifications.WaitForResourceHealthyAsync("huia-identityserver")
                .WaitAsync(TimeSpan.FromMinutes(3));
            await notifications.WaitForResourceAsync("shop-api", KnownResourceStates.Running)
                .WaitAsync(TimeSpan.FromMinutes(3));
            // The admin console is a Vite dev server (no health check); "Running" means the process is up.
            await notifications.WaitForResourceAsync("admin-app", KnownResourceStates.Running)
                .WaitAsync(TimeSpan.FromMinutes(3));

            // huia-external/shop-api/huia-identityserver/todo-api and every front-end app run unproxied on
            // the fixed dev ports AppHost.cs pins them to, so _app.GetEndpoint(...) isn't used here — it
            // reports an unproxied endpoint's raw bind address, which drops AppHost.cs's TargetHost alias
            // regardless. These are the same literals AppHost.cs itself uses: the front-end apps' own
            // *.dev.localhost origin (what their registered OIDC redirect URIs match, and what Playwright's
            // browser — which resolves *.localhost specially — should keep navigating directly), but plain
            // "localhost" for huia-external/shop-api/huia-identityserver/todo-api, since those are also
            // reached by server-side .NET/Node HTTP calls that don't get a browser's special-cased
            // "*.localhost is loopback" resolution (see the AppHost.cs comment above its own URL consts).
            Issuer = "https://localhost:5310";
            ExternalIssuer = "https://localhost:5320";
            ShopApiUrl = "https://localhost:5341";
            AdminAppUrl = "http://admin-app.dev.localhost:3001";
            TodoAppUrl = "http://todo-app.dev.localhost:3000";
            ShopAppUrl = "http://shop-app.dev.localhost:3002";
            TodoNextUrl = "http://todo-next.dev.localhost:3050";
            ShopNextUrl = "http://shop-next.dev.localhost:3060";
            Mailpit = new MailpitClient(_app.GetEndpoint("mailpit", "http").ToString().TrimEnd('/'));
            await Mailpit.WaitUntilReadyAsync();
            await WaitForAdminConsoleAsync();

            // Confirm the E2E tenant surface is actually on before the specs rely on it.
            using var probe = CreateIdpClient();
            using var discovery = await probe.GetAsync("e2e/.well-known/openid-configuration");
            if (!discovery.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"E2E tenant not available (discovery -> {(int)discovery.StatusCode}).");
            }

            Started = true;
        }
        catch (Exception ex)
        {
            SkipReason = $"AppHost did not start: {ex.Message}";
            Started = false;
        }
    }

    /// <summary>Polls the Nuxt dev server until it renders the landing page (first compile is slow).</summary>
    private async Task WaitForAdminConsoleAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var deadline = DateTime.UtcNow.AddMinutes(3);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var response = await http.GetAsync($"{Loopback(AdminAppUrl)}/");
                var body = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode && body.Contains("landing-sign-in", StringComparison.Ordinal))
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // not listening yet
            }
            catch (TaskCanceledException)
            {
                // http.Timeout elapsed on a single slow request (e.g. mid-compile under load) — not
                // fatal, just try again rather than aborting the whole fixture over one slow poll.
            }

            await Task.Delay(2_000);
        }

        throw new InvalidOperationException($"Admin console never became ready at {AdminAppUrl}.");
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }
}

[CollectionDefinition("apphost")]
public sealed class AppHostCollection : ICollectionFixture<AppHostFixture>;
