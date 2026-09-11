using System.Text.Json;
using Huia.OpenId.Options;
using Huia.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;

namespace Huia.OpenId.OpenIddict;

/// <summary>
/// Upserts an OpenIddict scope for every <see cref="HuiaScopeDescriptor"/> in the options tree. The
/// tenant binding goes into <c>Properties["huia:tenant"]</c>, mirroring <see cref="HuiaClientSeeder"/>.
/// A scope whose name is already claimed by a different tenant is skipped with a warning.
/// </summary>
/// <remarks>
/// When <see cref="Huia.Options.SeedingOptions.PruneRemovedStaticEntities"/> is on, also deletes a
/// static scope that no longer appears anywhere in the options tree — including one whose entire
/// tenant was removed. Deleting a scope is always safe: nothing references it by foreign key, only by
/// name, so at worst a client still requesting it gets <c>invalid_scope</c> afterwards.
/// </remarks>
internal sealed partial class HuiaScopeSeeder(
    IServiceProvider services, HuiaOptions options, ILogger<HuiaScopeSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();

        foreach (var (tenantId, tenant) in options.Tenants)
        {
            var openId = tenant.GetHuiaOpenId();
            if (openId is null)
            {
                continue;
            }

            foreach (var descriptor in openId.Scopes)
            {
                var existing = await manager.FindByNameAsync(descriptor.Name, cancellationToken);
                if (existing is not null
                    && ExtractProperty(await manager.GetPropertiesAsync(existing, cancellationToken),
                        HuiaOpenIdConstants.ApplicationProperties.Tenant) is { } owner
                    && !string.Equals(owner, tenantId, StringComparison.Ordinal))
                {
                    LogNameClash(descriptor.Name, tenantId, owner);
                    continue;
                }

                var model = BuildDescriptor(tenantId, descriptor);
                if (existing is null)
                {
                    await manager.CreateAsync(model, cancellationToken);
                    LogSeeded(descriptor.Name, tenantId);
                }
                else
                {
                    await manager.UpdateAsync(existing, model, cancellationToken);
                    LogUpdated(descriptor.Name, tenantId);
                }
            }
        }

        if (options.Seeding.PruneRemovedStaticEntities)
        {
            await PruneRemovedScopesAsync(manager, cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Deletes every static scope that no longer appears in the options tree. A single pass over
    /// <c>ListAsync</c> (which enumerates every scope regardless of tenant — scopes carry no per-tenant
    /// EF filter, only the <c>Properties</c> marker) so a scope whose entire tenant was removed from
    /// the options tree is caught too, not just one whose tenant is still configured with a shorter
    /// scope list.
    /// </summary>
    private async Task PruneRemovedScopesAsync(IOpenIddictScopeManager manager, CancellationToken cancellationToken)
    {
        await foreach (var existing in manager.ListAsync(null, null, cancellationToken))
        {
            var properties = await manager.GetPropertiesAsync(existing, cancellationToken);
            if (ExtractProperty(properties, HuiaOpenIdConstants.ApplicationProperties.Origin) != HuiaOpenIdConstants.Origins.Static)
            {
                continue;
            }

            var tenantId = ExtractProperty(properties, HuiaOpenIdConstants.ApplicationProperties.Tenant);
            var name = await manager.GetNameAsync(existing, cancellationToken);

            var stillDeclared = tenantId is not null
                && options.Tenants.TryGetValue(tenantId, out var tenant)
                && tenant.GetHuiaOpenId()?.Scopes.Any(s => string.Equals(s.Name, name, StringComparison.Ordinal)) == true;
            if (stillDeclared)
            {
                continue;
            }

            await manager.DeleteAsync(existing, cancellationToken);
            LogPruned(name ?? "(unnamed)", tenantId ?? "(unknown tenant)");
        }
    }

    private static string? ExtractProperty(IReadOnlyDictionary<string, JsonElement> properties, string key) =>
        properties.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static OpenIddictScopeDescriptor BuildDescriptor(string tenantId, HuiaScopeDescriptor scope)
    {
        var descriptor = new OpenIddictScopeDescriptor
        {
            Name = scope.Name,
            DisplayName = scope.DisplayName ?? scope.Name,
            Description = scope.Description,
        };

        foreach (var resource in scope.Resources)
        {
            descriptor.Resources.Add(resource);
        }

        descriptor.Properties[HuiaOpenIdConstants.ApplicationProperties.Tenant] =
            JsonSerializer.SerializeToElement(tenantId);
        descriptor.Properties[HuiaOpenIdConstants.ApplicationProperties.Origin] =
            JsonSerializer.SerializeToElement(HuiaOpenIdConstants.Origins.Static);

        return descriptor;
    }

    [LoggerMessage(LogLevel.Information, "Seeded OAuth scope {ScopeName} for tenant {TenantId}.")]
    partial void LogSeeded(string scopeName, string tenantId);

    [LoggerMessage(LogLevel.Information, "Updated OAuth scope {ScopeName} for tenant {TenantId}.")]
    partial void LogUpdated(string scopeName, string tenantId);

    [LoggerMessage(LogLevel.Warning,
        "OAuth scope {ScopeName} for tenant {TenantId} was skipped: the name is already owned by tenant {OwnerTenantId}.")]
    partial void LogNameClash(string scopeName, string tenantId, string ownerTenantId);

    [LoggerMessage(LogLevel.Information, "Pruned OAuth scope {ScopeName} for tenant {TenantId}: no longer declared in code.")]
    partial void LogPruned(string scopeName, string tenantId);
}
