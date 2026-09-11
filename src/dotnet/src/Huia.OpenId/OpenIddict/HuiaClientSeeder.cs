using System.Text.Json;
using Huia.OpenId.Options;
using Huia.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;

namespace Huia.OpenId.OpenIddict;

/// <summary>
/// Upserts an OpenIddict application for every <see cref="HuiaClientDescriptor"/> in the options tree.
/// The tenant binding goes into <c>Properties["huia:tenant"]</c>, the home / client / logo URIs into
/// the matching <c>huia:*</c> properties and the per-client token lifetimes into <c>Settings</c>.
/// </summary>
/// <remarks>
/// When <see cref="Huia.Options.SeedingOptions.PruneRemovedStaticEntities"/> is on, also deletes a
/// static client that no longer appears anywhere in the options tree — including one whose entire
/// tenant was removed. OpenIddict's store cascades this: it removes the application's authorizations
/// and tokens first, so revoking a client that has issued tokens is safe.
/// </remarks>
internal sealed partial class HuiaClientSeeder(
    IServiceProvider services, HuiaOptions options, ILogger<HuiaClientSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        foreach (var (tenantId, tenant) in options.Tenants)
        {
            var openId = tenant.GetHuiaOpenId();
            if (openId is null)
            {
                continue;
            }

            foreach (var client in openId.Clients)
            {
                var descriptor = HuiaApplicationDescriptorMapper.ToDescriptor(
                    tenantId, client, HuiaOpenIdConstants.Origins.Static);
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

        if (options.Seeding.PruneRemovedStaticEntities)
        {
            await PruneRemovedClientsAsync(manager, cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Deletes every static client that no longer appears in the options tree. A single pass over
    /// <c>ListAsync</c> (which enumerates every application regardless of tenant — applications carry
    /// no per-tenant EF filter, only the <c>Properties</c> marker) so a client whose entire tenant was
    /// removed from the options tree is caught too, not just one whose tenant is still configured with
    /// a shorter client list.
    /// </summary>
    private async Task PruneRemovedClientsAsync(IOpenIddictApplicationManager manager, CancellationToken cancellationToken)
    {
        await foreach (var existing in manager.ListAsync(null, null, cancellationToken))
        {
            var properties = await manager.GetPropertiesAsync(existing, cancellationToken);
            if (ExtractProperty(properties, HuiaOpenIdConstants.ApplicationProperties.Origin) != HuiaOpenIdConstants.Origins.Static)
            {
                continue;
            }

            var tenantId = ExtractProperty(properties, HuiaOpenIdConstants.ApplicationProperties.Tenant);
            var clientId = await manager.GetClientIdAsync(existing, cancellationToken);

            var stillDeclared = tenantId is not null
                && options.Tenants.TryGetValue(tenantId, out var tenant)
                && tenant.GetHuiaOpenId()?.Clients.Any(c => string.Equals(c.ClientId, clientId, StringComparison.Ordinal)) == true;
            if (stillDeclared)
            {
                continue;
            }

            await manager.DeleteAsync(existing, cancellationToken);
            LogPruned(clientId ?? "(unnamed)", tenantId ?? "(unknown tenant)");
        }
    }

    private static string? ExtractProperty(IReadOnlyDictionary<string, JsonElement> properties, string key) =>
        properties.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    [LoggerMessage(LogLevel.Information, "Seeded OAuth client {ClientId} for tenant {TenantId}.")]
    partial void LogSeeded(string clientId, string tenantId);

    [LoggerMessage(LogLevel.Information, "Updated OAuth client {ClientId} for tenant {TenantId}.")]
    partial void LogUpdated(string clientId, string tenantId);

    [LoggerMessage(LogLevel.Information, "Pruned OAuth client {ClientId} for tenant {TenantId}: no longer declared in code.")]
    partial void LogPruned(string clientId, string tenantId);
}
