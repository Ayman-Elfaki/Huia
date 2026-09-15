using Huia.Multitenancy;
using Huia.OpenId.EntityFrameworkCore;
using Huia.OpenId.EntityFrameworkCore.Entities;
using Huia.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Huia.OpenId.Identity;

/// <summary>
/// Ensures every role declared via <see cref="TenantOptions.AddRoles"/> exists for its tenant, stamped
/// <see cref="HuiaConstants.Origins.Static"/> — read-only in the admin API (rename / delete return
/// <c>409</c>), mirroring <see cref="Huia.OpenId.OpenIddict.HuiaClientSeeder"/> and
/// <see cref="Huia.OpenId.OpenIddict.HuiaScopeSeeder"/>. Only stamped at creation: a role that
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

            // A fresh scope per tenant, with the tenant entered BEFORE RoleManager (and hence
            // HuiaDbContext) is resolved — HuiaDbContext snapshots its tenant at construction. The
            // seeder is a singleton IHostedService, so IHuiaTenantContext (scoped) is resolved from
            // the fresh scope rather than injected into the constructor — a captive-dependency trap.
            await using var scope = services.CreateAsyncScope();
            using (scope.ServiceProvider.GetRequiredService<IHuiaTenantContext>().EnterTenantScope(scope.ServiceProvider, tenantId))
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

    /// <summary>
    /// Deletes every static role that no longer appears in the options tree's tenants. Discovery is a
    /// single cross-tenant read (via <c>IgnoreQueryFilters</c>, the same technique
    /// <c>AdminEndpoints.Roles.cs</c> uses) so a role whose entire tenant was removed from the options
    /// tree is caught too, not just one whose tenant is still configured with a shorter role list. The
    /// actual delete for each candidate re-enters that role's own tenant scope first — Finbuckle's
    /// <c>MultiTenantIdentityDbContext</c> refuses to save any change with no ambient tenant, even a
    /// delete found via <c>IgnoreQueryFilters</c>.
    /// </summary>
    private async Task PruneRemovedRolesAsync(CancellationToken cancellationToken)
    {
        var candidates = new List<(HuiaRole Role, bool HasMembers)>();
        await using (var readScope = services.CreateAsyncScope())
        {
            var db = readScope.ServiceProvider.GetRequiredService<HuiaDbContext>();
            var staticRoles = await db.Set<HuiaRole>().IgnoreQueryFilters()
                .Where(r => r.Origin == HuiaConstants.Origins.Static)
                .ToListAsync(cancellationToken);

            foreach (var role in staticRoles)
            {
                var stillDeclared = options.Tenants.TryGetValue(role.TenantId, out var tenant)
                    && tenant.Roles.Contains(role.Name ?? string.Empty, StringComparer.Ordinal);
                if (stillDeclared)
                {
                    continue;
                }

                var hasMembers = await db.Set<IdentityUserRole<string>>().IgnoreQueryFilters()
                    .AnyAsync(ur => ur.RoleId == role.Id, cancellationToken);
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
            using (scope.ServiceProvider.GetRequiredService<IHuiaTenantContext>().EnterTenantScope(scope.ServiceProvider, role.TenantId))
            {
                var db = scope.ServiceProvider.GetRequiredService<HuiaDbContext>();
                db.Remove(role);
                await db.SaveChangesAsync(cancellationToken);
            }

            LogPruned(role.Name ?? role.Id, role.TenantId);
        }
    }

    [LoggerMessage(LogLevel.Information, "Seeded role {RoleName} for tenant {TenantId}.")]
    partial void LogSeeded(string roleName, string tenantId);

    [LoggerMessage(LogLevel.Information, "Pruned role {RoleName} for tenant {TenantId}: no longer declared in code.")]
    partial void LogPruned(string roleName, string tenantId);

    [LoggerMessage(LogLevel.Warning,
        "Role {RoleName} for tenant {TenantId} is no longer declared in code but still has members; skipped.")]
    partial void LogPruneSkippedHasMembers(string roleName, string tenantId);
}
