using System.Text.Json;
using Huia.Multitenancy;
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
    IServiceProvider services, HuiaOptions options, IHuiaTenantProvider tenants, ILogger<HuiaScopeSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await services.EnsureTenantsLoadedAsync(cancellationToken);

        foreach (var (tenantId, tenant) in tenants)
        {
            await SeedTenantAsync(tenantId, tenant, cancellationToken);
        }

        if (options.Seeding.PruneRemovedStaticEntities)
        {
            await using var pruneScope = services.CreateAsyncScope();
            var manager = pruneScope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();
            await PruneRemovedScopesAsync(manager, cancellationToken);
        }
    }

    /// <summary>Upserts the OpenIddict scopes declared by one tenant (idempotent).</summary>
    public async Task SeedTenantAsync(string tenantId, HuiaTenant tenant, CancellationToken cancellationToken)
    {
        if (tenant.Scopes.Count == 0)
        {
            return;
        }

        // A fresh scope per tenant, with the tenant entered before the manager is resolved: scope names
        // are unique per tenant, so the lookup only sees this tenant's scopes.
        await using var tenantScope = services.CreateAsyncScope();
        using (tenantScope.ServiceProvider.GetRequiredService<IHuiaTenantContext>()
                   .EnterTenantScope(tenantScope.ServiceProvider, tenantId))
        {
            var manager = tenantScope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();
            foreach (var descriptor in tenant.Scopes)
            {
                var existing = await manager.FindByNameAsync(descriptor.Name, cancellationToken);
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
            if (ExtractProperty(properties, HuiaConstants.ApplicationProperties.Origin) != HuiaConstants.Origins.Static)
            {
                continue;
            }

            var tenantId = ExtractProperty(properties, HuiaConstants.ApplicationProperties.Tenant);
            var name = await manager.GetNameAsync(existing, cancellationToken);

            var stillDeclared = tenantId is not null
                && tenants.TryGetValue(tenantId, out var tenant)
                && tenant.Scopes.Any(s => string.Equals(s.Name, name, StringComparison.Ordinal));
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

        descriptor.Properties[HuiaConstants.ApplicationProperties.Tenant] =
            JsonSerializer.SerializeToElement(tenantId);
        descriptor.Properties[HuiaConstants.ApplicationProperties.Origin] =
            JsonSerializer.SerializeToElement(HuiaConstants.Origins.Static);

        return descriptor;
    }

    [LoggerMessage(LogLevel.Information, "Seeded OAuth scope {ScopeName} for tenant {TenantId}.")]
    partial void LogSeeded(string scopeName, string tenantId);

    [LoggerMessage(LogLevel.Information, "Updated OAuth scope {ScopeName} for tenant {TenantId}.")]
    partial void LogUpdated(string scopeName, string tenantId);

    [LoggerMessage(LogLevel.Information, "Pruned OAuth scope {ScopeName} for tenant {TenantId}: no longer declared in code.")]
    partial void LogPruned(string scopeName, string tenantId);
}
