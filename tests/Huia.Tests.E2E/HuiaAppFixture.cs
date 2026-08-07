namespace Huia.Tests.E2E;

/// <summary>
/// Starts the real <c>Huia.AppHost</c> distributed application (todoapi + mailpit + the Next.js web app +
/// the Nuxt admin-ui app) once and shares it across every test in the <c>"Huia E2E"</c> collection —
/// spinning up containers and running <c>npm install</c> per test would be far too slow.
/// </summary>
public sealed class HuiaAppFixture : IAsyncLifetime
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);

    public DistributedApplication App { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Huia_AppHost>();

        appHost.Services.ConfigureHttpClientDefaults(clientBuilder => clientBuilder.AddStandardResilienceHandler());

        App = await appHost.BuildAsync().WaitAsync(StartupTimeout);
        await App.StartAsync().WaitAsync(StartupTimeout);

        await App.ResourceNotifications.WaitForResourceHealthyAsync("mailpit").WaitAsync(StartupTimeout);
        await App.ResourceNotifications.WaitForResourceHealthyAsync("todoapi").WaitAsync(StartupTimeout);
        await App.ResourceNotifications.WaitForResourceHealthyAsync("web").WaitAsync(StartupTimeout);
        await App.ResourceNotifications.WaitForResourceHealthyAsync("admin-ui").WaitAsync(StartupTimeout);
    }

    public async Task DisposeAsync()
    {
        if (App is not null)
        {
            await App.DisposeAsync();
        }
    }
}

// Kept alongside HuiaAppFixture rather than in its own file - the idiomatic xUnit pairing of a collection
// fixture with the [CollectionDefinition] marker class that references it.
#pragma warning disable MA0048
[CollectionDefinition("Huia E2E")]
public sealed class HuiaAppCollection : ICollectionFixture<HuiaAppFixture>;