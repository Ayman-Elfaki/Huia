using Huia.Identity;
using MR.AspNetCore.Pagination;

namespace Huia.Endpoints;

/// <summary>Summary representation of a role returned by the admin API.</summary>
public sealed record RoleDto(string Id, string TenantId, string? Name, string Origin);

/// <summary>Detail representation of a role including member count.</summary>
public sealed record RoleDetailDto(string Id, string TenantId, string? Name, string Origin, int MemberCount);

/// <summary>Summary representation of a user account returned by the admin API.</summary>
public sealed record UserDto(
    string Id,
    string Tenant,
    string? UserName,
    string? Email,
    bool EmailConfirmed,
    string? PhoneNumber,
    bool PhoneNumberConfirmed,
    bool LockoutEnabled,
    DateTimeOffset? LockoutEnd,
    IReadOnlyList<string> Roles);

/// <summary>Storage abstraction for cross-tenant administrative queries on users and roles.</summary>
public interface IHuiaAdminStore
{
    /// <summary>Lists roles with keyset pagination, optionally filtered by tenant.</summary>
    Task<KeysetPaginationResult<RoleDto>> ListRolesAsync(string? tenantId, KeysetQueryModel query, CancellationToken cancellationToken = default);

    /// <summary>Gets role details by identifier, ignoring ambient tenant filters.</summary>
    Task<RoleDetailDto?> GetRoleDetailAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Gets the tenant identifier a role belongs to, ignoring ambient tenant filters.</summary>
    Task<string?> GetRoleTenantIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Gets the member count for a role, ignoring ambient tenant filters.</summary>
    Task<int> GetRoleMemberCountAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Lists users with keyset pagination, optionally filtered by tenant.</summary>
    Task<KeysetPaginationResult<UserDto>> ListUsersAsync(string? tenantId, KeysetQueryModel query, CancellationToken cancellationToken = default);

    /// <summary>Gets a user account by identifier, ignoring ambient tenant filters.</summary>
    Task<UserDto?> GetUserAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Gets the tenant identifier a user belongs to, ignoring ambient tenant filters.</summary>
    Task<string?> GetUserTenantIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Gets the roles assigned to a user, ignoring ambient tenant filters.</summary>
    Task<IReadOnlyList<string>> GetUserRolesAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Gets all static roles across all tenants, for start-up pruning.</summary>
    Task<IReadOnlyList<HuiaRole>> GetStaticRolesAsync(CancellationToken cancellationToken = default);

    /// <summary>Checks whether a role has any members assigned to it across all tenants.</summary>
    Task<bool> HasRoleMembersAsync(string roleId, CancellationToken cancellationToken = default);
}
