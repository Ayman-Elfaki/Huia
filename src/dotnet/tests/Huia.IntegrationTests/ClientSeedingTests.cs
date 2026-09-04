using System.Net;
using System.Text.Json;
using Huia.AspNetCore.OpenIddict;
using Huia.IntegrationTests.Infrastructure;
using Huia.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenIddict.Abstractions;

namespace Huia.IntegrationTests;

public sealed class ClientSeedingTests : IAsyncLifetime
{
    private HuiaTestHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await HuiaTestHost.StartAsync(configureOptions: huia =>
        {
            huia.AddTenant("seeded", tenant =>
            {
                tenant.Authentication.UseEmailAndPasswordLogin(password => password.RequireConfirmedEmail = false);
                tenant.AddMachineToMachineApplication("seeded-worker", "seeded-secret-value", client =>
                    client.Token.AccessToken = TimeSpan.FromMinutes(5));
                tenant.AddServerSideWebApplication("seeded-web", "seeded-web-secret", client =>
                {
                    client.RedirectUris.Add(new Uri("https://app.seeded.test/callback"));
                    client.ClientUri = new Uri("https://app.seeded.test/");
                    client.LogoUri = new Uri("https://cdn.seeded.test/logo.svg");
                });
            });
        });
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task A_declaratively_seeded_client_can_get_a_token_with_its_configured_lifetime()
    {
        var response = await _host.Client.PostAsync("/seeded/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "seeded-worker",
            ["client_secret"] = "seeded-secret-value",
        }));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var expiresIn = body.RootElement.GetProperty("expires_in").GetInt32();
        expiresIn.ShouldBeInRange(280, 300);
    }

    [Fact]
    public async Task Client_uri_and_logo_uri_are_written_to_the_application_properties()
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        var application = await manager.FindByClientIdAsync("seeded-web");
        application.ShouldNotBeNull();
        var properties = await manager.GetPropertiesAsync(application);

        properties["huia:client_uri"].GetString().ShouldBe("https://app.seeded.test/");
        properties["huia:logo_uri"].GetString().ShouldBe("https://cdn.seeded.test/logo.svg");
    }

    [Fact]
    public async Task A_declaratively_seeded_client_is_stamped_static()
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var app = await manager.FindByClientIdAsync("seeded-worker")
                  ?? throw new InvalidOperationException("The seeded client was not found.");

        var properties = await manager.GetPropertiesAsync(app);
        properties[HuiaConstants.ApplicationProperties.Origin].GetString().ShouldBe(HuiaConstants.Origins.Static);
    }

    // ------------------------------------------------------------------ pruning

    [Fact]
    public async Task Pruning_deletes_a_static_client_no_longer_declared_in_its_still_configured_tenant()
    {
        await CreatePhantomStaticClientAsync("ghost-client", "seeded");

        await RunClientSeederAsync(pruneRemovedStaticEntities: true);

        (await ClientExistsAsync("ghost-client")).ShouldBeFalse();
    }

    [Fact]
    public async Task Pruning_deletes_a_static_client_whose_entire_tenant_was_removed_from_config()
    {
        await CreatePhantomStaticClientAsync("ghost-client", "ghost-tenant");

        await RunClientSeederAsync(pruneRemovedStaticEntities: true);

        (await ClientExistsAsync("ghost-client")).ShouldBeFalse();
    }

    [Fact]
    public async Task Pruning_is_off_by_default_and_leaves_an_undeclared_static_client_alone()
    {
        await CreatePhantomStaticClientAsync("ghost-client", "seeded");

        await RunClientSeederAsync(pruneRemovedStaticEntities: false);

        (await ClientExistsAsync("ghost-client")).ShouldBeTrue();
    }

    /// <summary>Creates a client directly through the manager, bypassing the options tree — simulating
    /// "an earlier run declared this, the current code doesn't".</summary>
    private async Task CreatePhantomStaticClientAsync(string clientId, string tenantId)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var client = new HuiaClientDescriptor
        {
            ClientId = clientId,
            Kind = ClientKind.MachineToMachine,
            ClientSecret = "ghost-secret-value",
        };
        var descriptor = HuiaApplicationDescriptorMapper.ToDescriptor(tenantId, client, HuiaConstants.Origins.Static);
        await manager.CreateAsync(descriptor);
    }

    private async Task<bool> ClientExistsAsync(string clientId)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        return await manager.FindByClientIdAsync(clientId) is not null;
    }

    /// <summary>Re-runs the client seeder's start-up pass on demand, with the given pruning setting.</summary>
    private async Task RunClientSeederAsync(bool pruneRemovedStaticEntities)
    {
        _host.Services.GetRequiredService<HuiaOptions>().Seeding.PruneRemovedStaticEntities = pruneRemovedStaticEntities;
        var seeder = _host.Services.GetServices<IHostedService>().OfType<HuiaClientSeeder>().Single();
        await seeder.StartAsync(CancellationToken.None);
    }
}
