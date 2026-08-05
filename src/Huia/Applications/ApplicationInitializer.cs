using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenIddict.Abstractions;
using static Huia.Applications.ApplicationDescriptorFactory;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Huia.Applications;

/// <summary>
/// Idempotently upserts the client applications registered via <see cref="ApplicationsBuilder"/> into the
/// OpenIddict application store on startup.
/// </summary>
internal sealed class ApplicationInitializer(IServiceScopeFactory scopeFactory, ApplicationsBuilder applications)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        foreach (var app in applications.SinglePageApplications)
        {
            await SeedAsync(manager, BuildPublicInteractiveDescriptor(app, ApplicationTypes.Web),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var app in applications.NativeApplications)
        {
            await SeedAsync(manager, BuildPublicInteractiveDescriptor(app, ApplicationTypes.Native),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var app in applications.ServerSideWebApplicationOptions)
        {
            await SeedAsync(manager, BuildServerSideWebDescriptor(app), cancellationToken).ConfigureAwait(false);
        }

        foreach (var app in applications.MachineToMachineApplications)
        {
            await SeedAsync(manager, BuildMachineToMachineDescriptor(app), cancellationToken).ConfigureAwait(false);
        }

        foreach (var app in applications.DeviceApplications)
        {
            await SeedAsync(manager, BuildDeviceDescriptor(app), cancellationToken).ConfigureAwait(false);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}