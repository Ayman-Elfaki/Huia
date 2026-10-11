using Huia.Options;

namespace Huia.Stores;

/// <summary>A tenant read back from the persisted tenant store.</summary>
/// <param name="TenantId">The tenant id (also the base-path segment).</param>
/// <param name="Tenant">The reconstructed tenant configuration.</param>
/// <param name="Version">A counter that increases on every save, for change detection.</param>
/// <param name="UpdatedAt">When the tenant was last saved.</param>
public sealed record HuiaStoredTenant(string TenantId, HuiaTenant Tenant, long Version, DateTimeOffset UpdatedAt);

/// <summary>
/// Persistence port for runtime-defined tenants (those created through the admin API rather than declared in
/// code). Implementations validate and protect the configuration; callers see plain <see cref="HuiaTenant"/>s.
/// </summary>
public interface IHuiaTenantStore
{
    /// <summary>Returns every persisted tenant.</summary>
    Task<IReadOnlyList<HuiaStoredTenant>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns one persisted tenant, or <c>null</c>.</summary>
    Task<HuiaStoredTenant?> FindAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or replaces the persisted tenant <paramref name="tenantId"/> after validating it.
    /// </summary>
    /// <exception cref="HuiaOptionsException">The tenant does not validate.</exception>
    Task<HuiaStoredTenant> SaveAsync(string tenantId, HuiaTenant tenant, CancellationToken cancellationToken = default);

    /// <summary>Deletes the persisted tenant. Returns <c>false</c> when it did not exist.</summary>
    Task<bool> DeleteAsync(string tenantId, CancellationToken cancellationToken = default);
}
