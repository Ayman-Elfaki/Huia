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
    public string AdminAppUrl { get; private set; } = "";
    public MailpitClient Mailpit { get; private set; } = null!;

    /// <summary>An <see cref="HttpClient"/> pointed at the identity server, accepting its dev certificate.</summary>
    public HttpClient CreateIdpClient()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            UseCookies = true,
            CookieContainer = new System.Net.CookieContainer(),
            AllowAutoRedirect = false,
        };
        return new HttpClient(handler) { BaseAddress = new Uri(Issuer + "/") };
    }

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
            await notifications.WaitForResourceHealthyAsync("huia-identityserver")
                .WaitAsync(TimeSpan.FromMinutes(3));
            // The admin console is a Vite dev server (no health check); "Running" means the process is up.
            await notifications.WaitForResourceAsync("admin-app", KnownResourceStates.Running)
                .WaitAsync(TimeSpan.FromMinutes(3));

            Issuer = _app.GetEndpoint("huia-identityserver", "https").ToString().TrimEnd('/');
            // The admin console runs unproxied on its fixed port (see AppHost.cs) so its origin matches
            // the seeded OIDC client's redirect URI — the nuxt-oidc-auth state cookie is per-origin.
            AdminAppUrl = _app.GetEndpoint("admin-app", "http").ToString().TrimEnd('/');
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
                using var response = await http.GetAsync($"{AdminAppUrl}/");
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
