using Huia.Endpoints;
using Huia.EntityFrameworkCore.Entities;
using Huia.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MR.AspNetCore.Pagination;

namespace Huia.EntityFrameworkCore.Stores;

/// <summary>EF Core implementation of <see cref="IHuiaAdminStore"/>.</summary>
public class EfHuiaAdminStore(HuiaDbContext db, IPaginationService pagination) : IHuiaAdminStore
{
    /// <inheritdoc />
    public async Task<KeysetPaginationResult<RoleDto>> ListRolesAsync(string? tenantId, KeysetQueryModel query, CancellationToken cancellationToken = default)
    {
        var source = db.Set<HuiaRole>().IgnoreQueryFilters().AsNoTracking();
        if (!string.IsNullOrEmpty(tenantId))
        {
            source = source.Where(r => r.TenantId == tenantId);
        }

        return await pagination.KeysetPaginateAsync(
            source,
            builder => builder.Ascending(r => r.Id),
            async id => await db.Set<HuiaRole>().IgnoreQueryFilters().FirstOrDefaultAsync(r => r.Id == id, cancellationToken),
            roles => roles.Select(r => new RoleDto(r.Id, r.TenantId, r.Name, r.Origin)),
            query);
    }

    /// <inheritdoc />
    public async Task<RoleDetailDto?> GetRoleDetailAsync(string id, CancellationToken cancellationToken = default)
    {
        var role = await db.Set<HuiaRole>().IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (role is null)
        {
            return null;
        }

        var members = await db.Set<IdentityUserRole<string>>().IgnoreQueryFilters().AsNoTracking()
            .CountAsync(ur => ur.RoleId == id, cancellationToken);

        return new RoleDetailDto(role.Id, role.TenantId, role.Name, role.Origin, members);
    }

    /// <inheritdoc />
    public async Task<string?> GetRoleTenantIdAsync(string id, CancellationToken cancellationToken = default)
    {
        return await db.Set<HuiaRole>().IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => r.TenantId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> GetRoleMemberCountAsync(string id, CancellationToken cancellationToken = default)
    {
        return await db.Set<IdentityUserRole<string>>().IgnoreQueryFilters().AsNoTracking()
            .CountAsync(ur => ur.RoleId == id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<KeysetPaginationResult<UserDto>> ListUsersAsync(string? tenantId, KeysetQueryModel query, CancellationToken cancellationToken = default)
    {
        var source = db.Set<HuiaUser>().IgnoreQueryFilters().AsNoTracking();
        if (!string.IsNullOrEmpty(tenantId))
        {
            source = source.Where(u => u.TenantId == tenantId);
        }

        var paginated = await pagination.KeysetPaginateAsync(
            source,
            builder => builder.Ascending(u => u.Id),
            async id => await db.Set<HuiaUser>().IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == id, cancellationToken),
            query);

        var userIds = paginated.Data.Select(u => u.Id).ToList();
        var rolesByUser = await RolesByUserAsync(userIds, cancellationToken);

        var dtos = paginated.Data.Select(u => new UserDto(
            u.Id,
            u.TenantId,
            u.UserName,
            u.Email,
            u.EmailConfirmed,
            u.PhoneNumber,
            u.PhoneNumberConfirmed,
            u.LockoutEnabled,
            u.LockoutEnd,
            rolesByUser.GetValueOrDefault(u.Id, []))).ToList();

        return new KeysetPaginationResult<UserDto>(dtos, paginated.PageSize, paginated.TotalCount, paginated.HasPrevious, paginated.HasNext);
    }

    /// <inheritdoc />
    public async Task<UserDto?> GetUserAsync(string id, CancellationToken cancellationToken = default)
    {
        var user = await db.Set<HuiaUser>().IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var rolesByUser = await RolesByUserAsync([id], cancellationToken);
        var roles = rolesByUser.GetValueOrDefault(id, []);

        return new UserDto(
            user.Id,
            user.TenantId,
            user.UserName,
            user.Email,
            user.EmailConfirmed,
            user.PhoneNumber,
            user.PhoneNumberConfirmed,
            user.LockoutEnabled,
            user.LockoutEnd,
            roles);
    }

    /// <inheritdoc />
    public async Task<string?> GetUserTenantIdAsync(string id, CancellationToken cancellationToken = default)
    {
        return await db.Set<HuiaUser>().IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => u.TenantId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetUserRolesAsync(string userId, CancellationToken cancellationToken = default)
    {
        var rolesByUser = await RolesByUserAsync([userId], cancellationToken);
        return rolesByUser.GetValueOrDefault(userId, []);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Huia.Identity.HuiaRole>> GetStaticRolesAsync(CancellationToken cancellationToken = default)
    {
        return await db.Set<HuiaRole>().IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.Origin == HuiaConstants.Origins.Static)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> HasRoleMembersAsync(string roleId, CancellationToken cancellationToken = default)
    {
        return await db.Set<IdentityUserRole<string>>().IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(ur => ur.RoleId == roleId, cancellationToken);
    }

    private async Task<Dictionary<string, List<string>>> RolesByUserAsync(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return [];
        }

        var userRoles = await db.Set<IdentityUserRole<string>>().IgnoreQueryFilters().AsNoTracking()
            .Where(ur => userIds.Contains(ur.UserId))
            .ToListAsync(cancellationToken);

        var roleIds = userRoles.Select(ur => ur.RoleId).Distinct().ToList();
        var roleNames = await db.Set<HuiaRole>().IgnoreQueryFilters().AsNoTracking()
            .Where(r => roleIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Name ?? r.Id, cancellationToken);

        return userRoles
            .GroupBy(ur => ur.UserId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(ur => roleNames.GetValueOrDefault(ur.RoleId, ur.RoleId)).OrderBy(n => n).ToList());
    }
}
