using Huia.Entities;

namespace Huia.Stores;

/// <summary>
/// Provides paginated user/role list queries and batch role lookups needed by Huia's admin endpoints.
/// Implementations ship in the storage-specific packages:
/// <c>Huia.Headless.EntityFrameworkCore</c> and <c>Huia.OpenId.EntityFrameworkCore</c>.
/// Custom stores (e.g., Dapper) implement this interface and register it in DI before calling
/// <c>AddHuiaHeadless()</c> or <c>AddHuiaOpenId()</c>.
/// </summary>
/// <typeparam name="TUser">The concrete user type, at least as derived as <see cref="HuiaUser"/>.</typeparam>
/// <typeparam name="TRole">The concrete role type, at least as derived as <see cref="HuiaRole"/>.</typeparam>
public interface IHuiaStore<TUser, TRole>
    where TUser : HuiaUser
    where TRole : HuiaRole
{
    /// <summary>Returns a page of users, with optional tenant and text-search filters.</summary>
    Task<IHuiaPageResult<TUser>> ListUsersAsync(HuiaUserQuery query, CancellationToken cancellationToken = default);

    /// <summary>Returns a page of roles, with optional tenant filter.</summary>
    Task<IHuiaPageResult<TRole>> ListRolesAsync(HuiaRoleQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a dictionary mapping each user ID to the names of the roles that user belongs to.
    /// Missing user IDs produce no entry (not a null/empty array) in the result.
    /// </summary>
    Task<IReadOnlyDictionary<string, string[]>> GetRolesByUserIdsAsync(
        IReadOnlyCollection<string> userIds, CancellationToken cancellationToken = default);
}
