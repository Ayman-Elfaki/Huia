using Huia.Endpoints;
using Huia.Multitenancy;
using Huia.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Huia.Identity;

/// <summary>
/// Ensures every role declared via <see cref="TenantOptions.AddRoles"/> exists for its tenant, stamped
/// <see cref="HuiaConstants.Origins.Static"/> — read-only in the admin API (rename / delete return
/// <c>409</c>). Only stamped at creation: a role that
/// already exists under that name (created dynamically before the code declaration was added) is left
/// alone rather than having its origin silently flipped.
/// </summary>
/// <remarks>
/// When <see cref="SeedingOptions.PruneRemovedStaticEntities"/> is on, also deletes a static role that
/// no longer appears anywhere in the options tree — including one whose entire tenant was removed. A
/// static role that still has members is skipped (and logged) rather than force-deleted.
/// </remarks>
internal sealed partial class HuiaRoleSeeder(
    IServiceProvider services, HuiaOptions options, ILogger<HuiaRoleSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var (tenantId, tenant) in options.Tenants)
        {
            if (tenant.Roles.Count == 0)
            {
                continue;
            }

            await using var scope = services.CreateAsyncScope();
            using (HuiaTenantScope.Enter(scope.ServiceProvider, tenantId))
            {
                var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<HuiaRole>>();
                foreach (var roleName in tenant.Roles)
                {
                    if (await roleManager.RoleExistsAsync(roleName))
                    {
                        continue;
                    }

                    await roleManager.CreateAsync(
                        new HuiaRole(roleName) { TenantId = tenantId, Origin = HuiaConstants.Origins.Static });
                    LogSeeded(roleName, tenantId);
                }
            }
        }

        if (options.Seeding.PruneRemovedStaticEntities)
        {
            await PruneRemovedRolesAsync(cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task PruneRemovedRolesAsync(CancellationToken cancellationToken)
    {
        var candidates = new List<(HuiaRole Role, bool HasMembers)>();
        await using (var readScope = services.CreateAsyncScope())
        {
            var adminStore = readScope.ServiceProvider.GetService<IHuiaAdminStore>();
            if (adminStore is null)
            {
                return;
            }

            var staticRoles = await adminStore.GetStaticRolesAsync(cancellationToken);

            foreach (var role in staticRoles)
            {
                var stillDeclared = options.Tenants.TryGetValue(role.TenantId, out var tenant)
                    && tenant.Roles.Contains(role.Name ?? string.Empty, StringComparer.Ordinal);
                if (stillDeclared)
                {
                    continue;
                }

                var hasMembers = await adminStore.HasRoleMembersAsync(role.Id, cancellationToken);
                candidates.Add((role, hasMembers));
            }
        }

        foreach (var (role, hasMembers) in candidates)
        {
            if (hasMembers)
            {
                LogPruneSkippedHasMembers(role.Name ?? role.Id, role.TenantId);
                continue;
            }

            await using var scope = services.CreateAsyncScope();
            using (HuiaTenantScope.Enter(scope.ServiceProvider, role.TenantId))
            {
                var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<HuiaRole>>();
                await roleManager.DeleteAsync(role);
            }

            LogPruned(role.Name ?? role.Id, role.TenantId);
        }
    }

    [LoggerMessage(LogLevel.Information, "Seeded role {RoleName} for tenant {TenantId}.")]
    partial void LogSeeded(string roleName, string tenantId);

    [LoggerMessage(LogLevel.Information, "Pruned role {RoleName} for tenant {TenantId}: no longer declared in code.")]
    partial void LogPruned(string roleName, string tenantId);

    [LoggerMessage(LogLevel.Warning, "Skipped pruning role {RoleName} for tenant {TenantId}: still has user members.")]
    partial void LogPruneSkippedHasMembers(string roleName, string tenantId);
}
