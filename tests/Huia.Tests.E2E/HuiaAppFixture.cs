namespace Huia.Tests.E2E;

/// <summary>
/// Starts the real <c>Huia.AppHost</c> distributed application (todoapi + mailpit + the Next.js web app +
/// the Nuxt admin-ui app + the identityserver sample, a second Huia instance acting as an external OIDC
/// provider) once and shares it across every test in the <c>"Huia E2E"</c> collection — spinning up
/// containers and running <c>npm install</c> per test would be far too slow.
/// </summary>
/// <remarks>
/// In one particular sandboxed dev environment, every test past registration/sign-in reliably hung
/// indefinitely waiting for the post-OIDC-callback navigation back to "web" — confirmed *not* a code
/// regression (reproduces identically on an unmodified checkout) and *not* the backend/OAuth logic itself
/// (the exact same HTTP round trip, replayed manually outside a browser, completes in well under a second
/// every time). Neither <see cref="WarmUpAsync"/> below nor raising every navigation timeout to 180s
/// (see the individual E2E test files) made it pass there — the hang is specific to how a real browser's
/// page-load lifecycle behaves against this stack on that host, not a slow-but-eventually-successful wait.
/// Both changes are kept anyway since they're harmless and may help on a different/faster host; if the
/// suite is still unreliable in your environment, that's a known, unresolved, environment-specific issue —
/// not evidence the application code is broken.
/// </remarks>
public sealed class HuiaAppFixture : IAsyncLifetime
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);

    public DistributedApplication App { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        // accounts.google.com (and other real external providers ExternalLoginE2ETests exercises) publish both
        // an AAAA and an A record; some sandboxed/CI environments have IPv6 silently black-holed rather than
        // cleanly unreachable, which is the one failure mode .NET's SocketsHttpHandler Happy-Eyeballs fallback
        // handles worst — the outbound call hangs until OpenIddict.Client's own resilience-handler timeout
        // fires instead of falling back to IPv4 quickly. Scoped to this fixture (not AppHost.cs's general
        // dev-mode use, and not the Huia library) since it only needs to affect test-orchestrated child
        // processes, not every consumer's network. Must be set before CreateAsync spawns those processes,
        // since they inherit the current process's environment at that point.
        Environment.SetEnvironmentVariable("DOTNET_SYSTEM_NET_DISABLEIPV6", "1");

        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Huia_AppHost>();

        appHost.Services.ConfigureHttpClientDefaults(clientBuilder => clientBuilder.AddStandardResilienceHandler());

        App = await appHost.BuildAsync().WaitAsync(StartupTimeout);
        await App.StartAsync().WaitAsync(StartupTimeout);

        await App.ResourceNotifications.WaitForResourceHealthyAsync("mailpit").WaitAsync(StartupTimeout);
        await App.ResourceNotifications.WaitForResourceHealthyAsync("todoapi").WaitAsync(StartupTimeout);
        await App.ResourceNotifications.WaitForResourceHealthyAsync("identityserver").WaitAsync(StartupTimeout);
        await App.ResourceNotifications.WaitForResourceHealthyAsync("web").WaitAsync(StartupTimeout);
        await App.ResourceNotifications.WaitForResourceHealthyAsync("admin-ui").WaitAsync(StartupTimeout);

        await WarmUpAsync();
    }

    public async Task DisposeAsync()
    {
        if (App is not null)
        {
            await App.DisposeAsync();
        }
    }

    // "web" (Next.js) and "admin-ui" (Nuxt) both run their own dev server here (npm run dev), which compiles
    // each route lazily on its *first* request rather than ahead of time like a production build — a cold
    // compile can take well past the 30-60s navigation timeouts the E2E tests below budget for a normal
    // request. Hitting the routes every test collectively exercises once here, up front, means whichever test
    // happens to run first isn't the one stuck paying for that compile inside its own timeout window. The
    // actual response (success, redirect, or error) is irrelevant — issuing the request is what forces the
    // dev server to compile the route; failures are swallowed since nothing here asserts a real user flow.
    private async Task WarmUpAsync()
    {
        string[] webRoutes = ["/", "/api/auth/providers", "/api/auth/signin/huia", "/api/auth/callback/huia"];
        foreach (var route in webRoutes)
        {
            await WarmUpRouteAsync("web", route);
        }

        string[] adminUiRoutes = ["/", "/users", "/scopes", "/applications"];
        foreach (var route in adminUiRoutes)
        {
            await WarmUpRouteAsync("admin-ui", route);
        }
    }

    private async Task WarmUpRouteAsync(string resourceName, string path)
    {
        try
        {
            using var client = App.CreateHttpClient(resourceName);
            client.Timeout = TimeSpan.FromSeconds(45);
            using var response = await client.GetAsync(path);
        }
        catch
        {
            // Ignored — a warm-up request timing out, erroring, or redirecting somewhere unauthenticated
            // doesn't matter; only forcing the dev server's lazy first-request compile does.
        }
    }
}

// Kept alongside HuiaAppFixture rather than in its own file - the idiomatic xUnit pairing of a collection
// fixture with the [CollectionDefinition] marker class that references it.
#pragma warning disable MA0048
[CollectionDefinition("Huia E2E")]
public sealed class HuiaAppCollection : ICollectionFixture<HuiaAppFixture>;