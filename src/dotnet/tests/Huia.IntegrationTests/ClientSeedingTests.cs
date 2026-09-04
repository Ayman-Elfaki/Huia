using System.Net;
using System.Text.Json;
using Huia.IntegrationTests.Infrastructure;
using Huia.Options;
using Microsoft.Extensions.DependencyInjection;
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
}
