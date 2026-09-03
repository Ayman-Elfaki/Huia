using System.Text.Json;
using Huia.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;

namespace Huia.AspNetCore.OpenIddict;

/// <summary>
/// Upserts an OpenIddict scope for every <see cref="HuiaScopeDescriptor"/> in the options tree. The
/// tenant binding goes into <c>Properties["huia:tenant"]</c>, mirroring <see cref="HuiaClientSeeder"/>.
/// A scope whose name is already claimed by a different tenant is skipped with a warning.
/// </summary>
internal sealed partial class HuiaScopeSeeder(
    IServiceProvider services, HuiaOptions options, ILogger<HuiaScopeSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();

        foreach (var (tenantId, tenant) in options.Tenants)
        {
            foreach (var descriptor in tenant.Scopes)
            {
                var existing = await manager.FindByNameAsync(descriptor.Name, cancellationToken);
                if (existing is not null && await OwnerTenantAsync(manager, existing, cancellationToken) is { } owner
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
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async ValueTask<string?> OwnerTenantAsync(
        IOpenIddictScopeManager manager, object scope, CancellationToken cancellationToken)
    {
        var properties = await manager.GetPropertiesAsync(scope, cancellationToken);
        return properties.TryGetValue(HuiaConstants.ApplicationProperties.Tenant, out var value)
               && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

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

    [LoggerMessage(LogLevel.Warning,
        "OAuth scope {ScopeName} for tenant {TenantId} was skipped: the name is already owned by tenant {OwnerTenantId}.")]
    partial void LogNameClash(string scopeName, string tenantId, string ownerTenantId);
}
