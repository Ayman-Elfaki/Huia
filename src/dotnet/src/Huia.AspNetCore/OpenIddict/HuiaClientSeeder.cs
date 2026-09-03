using Huia.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;

namespace Huia.AspNetCore.OpenIddict;

/// <summary>
/// Upserts an OpenIddict application for every <see cref="HuiaClientDescriptor"/> in the options tree.
/// The tenant binding goes into <c>Properties["huia:tenant"]</c>, the home / client / logo URIs into
/// the matching <c>huia:*</c> properties and the per-client token lifetimes into <c>Settings</c>.
/// </summary>
internal sealed partial class HuiaClientSeeder(
    IServiceProvider services, HuiaOptions options, ILogger<HuiaClientSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        foreach (var (tenantId, tenant) in options.Tenants)
        {
            foreach (var client in tenant.Clients)
            {
                var descriptor = HuiaApplicationDescriptorMapper.ToDescriptor(
                    tenantId, client, HuiaConstants.Origins.Static);
                var existing = await manager.FindByClientIdAsync(client.ClientId, cancellationToken);
                if (existing is null)
                {
                    await manager.CreateAsync(descriptor, cancellationToken);
                    LogSeeded(client.ClientId, tenantId);
                }
                else
                {
                    await manager.UpdateAsync(existing, descriptor, cancellationToken);
                    LogUpdated(client.ClientId, tenantId);
                }
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(LogLevel.Information, "Seeded OAuth client {ClientId} for tenant {TenantId}.")]
    partial void LogSeeded(string clientId, string tenantId);

    [LoggerMessage(LogLevel.Information, "Updated OAuth client {ClientId} for tenant {TenantId}.")]
    partial void LogUpdated(string clientId, string tenantId);
}
