using Huia.Entities;
using Huia.Stores;

namespace Huia.Stores;

/// <summary>
/// Provides the cross-cutting Identity queries needed by <c>Huia.OpenId</c>'s admin endpoints that
/// previously went directly to <c>HuiaDbContext</c>. The EF Core implementation ships in
/// <c>Huia.OpenId.EntityFrameworkCore</c>; custom stores (e.g., Dapper) implement this interface
/// and register it with <c>services.AddScoped&lt;IHuiaOpenIdAdminStore&lt;HuiaUser, HuiaRole&gt;, MyStore&gt;()</c>
/// before calling <c>AddEntityFrameworkCoreStores()</c>.
/// </summary>
/// <typeparam name="TUser">The concrete user type, at least as derived as <see cref="HuiaUser"/>.</typeparam>
/// <typeparam name="TRole">The concrete role type, at least as derived as <see cref="HuiaRole"/>.</typeparam>
public interface IHuiaOpenIdAdminStore<TUser, TRole>
    where TUser : HuiaUser
    where TRole : HuiaRole
{
    // -----------------------------------------------------------------------------------------
    // Paginated lists (admin console)
    // -----------------------------------------------------------------------------------------

    /// <summary>Returns a keyset-paginated page of users, optionally filtered by tenant.</summary>
    Task<IHuiaPageResult<TUser>> ListUsersAsync(HuiaUserQuery query, CancellationToken ct = default);

    /// <summary>Returns a keyset-paginated page of roles, optionally filtered by tenant.</summary>
    Task<IHuiaPageResult<TRole>> ListRolesAsync(HuiaRoleQuery query, CancellationToken ct = default);

    /// <summary>
    /// Returns the role names for each supplied user ID in one round-trip,
    /// bypassing any per-tenant query filter.
    /// </summary>
    Task<IReadOnlyDictionary<string, string[]>> GetRolesByUserIdsAsync(
        IReadOnlyCollection<string> userIds, CancellationToken ct = default);

    // -----------------------------------------------------------------------------------------
    // Per-row lookups (admin CRUD — previously db.Set<T>().IgnoreQueryFilters())
    // -----------------------------------------------------------------------------------------

    /// <summary>Finds a user by ID across all tenants (bypassing query filters). Returns <c>null</c> if not found.</summary>
    Task<TUser?> FindUserByIdAsync(string userId, CancellationToken ct = default);

    /// <summary>Returns the <c>TenantId</c> of the user with the given ID, bypassing query filters. Returns <c>null</c> if not found.</summary>
    Task<string?> GetUserTenantIdAsync(string userId, CancellationToken ct = default);

    /// <summary>Returns the <c>TenantId</c> of the role with the given ID, bypassing query filters. Returns <c>null</c> if not found.</summary>
    Task<string?> GetRoleTenantIdAsync(string roleId, CancellationToken ct = default);

    /// <summary>Returns the <c>TenantId</c> and <c>Origin</c> of the role with the given ID, bypassing query filters.</summary>
    Task<(string? TenantId, string? Origin)> GetRoleMetadataAsync(string roleId, CancellationToken ct = default);

    /// <summary>Returns the number of users assigned to the role with the given ID, bypassing query filters.</summary>
    Task<int> GetRoleMemberCountAsync(string roleId, CancellationToken ct = default);

    /// <summary>Returns <c>true</c> if any user is assigned to the role with the given ID, bypassing query filters.</summary>
    Task<bool> IsRoleAssignedToAnyUserAsync(string roleId, CancellationToken ct = default);
}
