using Finbuckle.MultiTenant.Identity.EntityFrameworkCore;
using Huia.OpenId.EntityFrameworkCore.Entities;
using Huia.Stores;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MR.AspNetCore.Pagination;

namespace Huia.OpenId.EntityFrameworkCore;

/// <summary>
/// EF Core implementation of <see cref="IHuiaOpenIdAdminStore{TUser,TRole}"/>. Uses keyset pagination
/// via <c>MR.AspNetCore.Pagination</c> for list endpoints, and <c>IgnoreQueryFilters()</c> for the
/// cross-tenant lookups the admin endpoints need.
/// </summary>
internal sealed class HuiaOpenIdEfCoreAdminStore<TContext, TUser, TRole>(
    TContext db,
    IPaginationService pagination)
    : IHuiaOpenIdAdminStore<TUser, TRole>
    where TContext : DbContext
    where TUser : HuiaUser
    where TRole : HuiaRole
{
    // -----------------------------------------------------------------------------------------
    // Paginated lists
    // -----------------------------------------------------------------------------------------

    public async Task<IHuiaPageResult<TUser>> ListUsersAsync(HuiaUserQuery query, CancellationToken ct)
    {
        var source = db.Set<TUser>().IgnoreQueryFilters().AsNoTracking();

        if (!string.IsNullOrEmpty(query.TenantId))
        {
            source = source.Where(u => u.TenantId == query.TenantId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim();
            source = source.Where(u =>
                (u.Email != null && u.Email.Contains(s)) ||
                (u.UserName != null && u.UserName.Contains(s)) ||
                (u.FirstName != null && u.FirstName.Contains(s)) ||
                (u.LastName != null && u.LastName.Contains(s)) ||
                (u.PhoneNumber != null && u.PhoneNumber.Contains(s)));
        }

        if (!string.IsNullOrWhiteSpace(query.UserName))
        {
            var username = query.UserName.Trim();
            source = source.Where(u => u.UserName != null && u.UserName.Contains(username));
        }

        if (!string.IsNullOrWhiteSpace(query.FirstName))
        {
            var firstName = query.FirstName.Trim();
            source = source.Where(u => u.FirstName != null && u.FirstName.Contains(firstName));
        }

        if (!string.IsNullOrWhiteSpace(query.LastName))
        {
            var lastName = query.LastName.Trim();
            source = source.Where(u => u.LastName != null && u.LastName.Contains(lastName));
        }

        if (!string.IsNullOrWhiteSpace(query.PhoneNumber))
        {
            var phone = query.PhoneNumber.Trim();
            source = source.Where(u => u.PhoneNumber != null && u.PhoneNumber.Contains(phone));
        }

        if (!string.IsNullOrWhiteSpace(query.Email))
        {
            var email = query.Email.Trim();
            source = source.Where(u => u.Email != null && u.Email.Contains(email));
        }

        if (query.EmailConfirmed.HasValue)
        {
            source = source.Where(u => u.EmailConfirmed == query.EmailConfirmed.Value);
        }

        if (query.PhoneNumberConfirmed.HasValue)
        {
            source = source.Where(u => u.PhoneNumberConfirmed == query.PhoneNumberConfirmed.Value);
        }

        if (query.IsLockedOut.HasValue)
        {
            var isSqlite = db.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;
            if (isSqlite)
            {
                source = query.IsLockedOut.Value
                    ? source.Where(u => u.LockoutEnd != null)
                    : source.Where(u => u.LockoutEnd == null);
            }
            else
            {
                var now = DateTimeOffset.UtcNow;
                source = query.IsLockedOut.Value
                    ? source.Where(u => u.LockoutEnd != null && u.LockoutEnd > now)
                    : source.Where(u => u.LockoutEnd == null || u.LockoutEnd <= now);
            }
        }

        var roles = (query.Roles ?? (string.IsNullOrWhiteSpace(query.Role) ? null : [query.Role]))?
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .ToList();

        if (roles is { Count: > 0 })
        {
            var roleQuery = db.Set<TRole>().IgnoreQueryFilters().AsNoTracking().Where(r => roles.Contains(r.Name!));
            if (!string.IsNullOrEmpty(query.TenantId))
            {
                roleQuery = roleQuery.Where(r => r.TenantId == query.TenantId);
            }

            var matchingUserIds = db.Set<IdentityUserRole<string>>().IgnoreQueryFilters().AsNoTracking()
                .Join(roleQuery, ur => ur.RoleId, r => r.Id, (ur, r) => ur.UserId);

            source = source.Where(u => matchingUserIds.Contains(u.Id));
        }

        var result = await pagination.KeysetPaginateAsync(
            source,
            builder => builder.Ascending(u => u.Id),
            async id => await db.Set<TUser>().IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == id, ct),
            users => users,
            BuildKeysetQuery(query));

        return new HuiaEfPageResult<TUser>(result, u => u.Id);
    }

    public async Task<IHuiaPageResult<TRole>> ListRolesAsync(HuiaRoleQuery query, CancellationToken ct)
    {
        var source = db.Set<TRole>().IgnoreQueryFilters().AsNoTracking();

        if (!string.IsNullOrEmpty(query.TenantId))
        {
            source = source.Where(r => r.TenantId == query.TenantId);
        }

        var result = await pagination.KeysetPaginateAsync(
            source,
            builder => builder.Ascending(r => r.Id),
            async id => await db.Set<TRole>().IgnoreQueryFilters().FirstOrDefaultAsync(r => r.Id == id, ct),
            roles => roles,
            BuildKeysetQuery(query));

        return new HuiaEfPageResult<TRole>(result, r => r.Id);
    }

    public async Task<IReadOnlyDictionary<string, string[]>> GetRolesByUserIdsAsync(
        IReadOnlyCollection<string> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<string, string[]>();
        }

        var rows = await db.Set<IdentityUserRole<string>>().IgnoreQueryFilters().AsNoTracking()
            .Where(ur => userIds.Contains(ur.UserId))
            .Join(db.Set<TRole>().IgnoreQueryFilters().AsNoTracking(),
                ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .ToListAsync(ct);

        return rows
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Name!).ToArray());
    }

    // -----------------------------------------------------------------------------------------
    // Per-row lookups (cross-tenant, IgnoreQueryFilters)
    // -----------------------------------------------------------------------------------------

    public Task<TUser?> FindUserByIdAsync(string userId, CancellationToken ct)
        => db.Set<TUser>().IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

    public Task<string?> GetUserTenantIdAsync(string userId, CancellationToken ct)
        => db.Set<TUser>().IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.TenantId)
            .FirstOrDefaultAsync(ct);

    public Task<string?> GetRoleTenantIdAsync(string roleId, CancellationToken ct)
        => db.Set<TRole>().IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.Id == roleId)
            .Select(r => r.TenantId)
            .FirstOrDefaultAsync(ct);

    public async Task<(string? TenantId, string? Origin)> GetRoleMetadataAsync(string roleId, CancellationToken ct)
    {
        var row = await db.Set<TRole>().IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.Id == roleId)
            .Select(r => new { r.TenantId, r.Origin })
            .FirstOrDefaultAsync(ct);

        return row is null ? (null, null) : (row.TenantId, row.Origin);
    }

    public Task<int> GetRoleMemberCountAsync(string roleId, CancellationToken ct)
        => db.Set<IdentityUserRole<string>>().IgnoreQueryFilters().AsNoTracking()
            .CountAsync(ur => ur.RoleId == roleId, ct);

    public Task<bool> IsRoleAssignedToAnyUserAsync(string roleId, CancellationToken ct)
        => db.Set<IdentityUserRole<string>>().IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(ur => ur.RoleId == roleId, ct);

    // -----------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------

    private static KeysetQueryModel BuildKeysetQuery(HuiaUserQuery q) => new()
    {
        Size = q.PageSize,
        After = q.After,
        Before = q.Before,
        First = q.After == null && q.Before == null,
        Last = false,
    };

    private static KeysetQueryModel BuildKeysetQuery(HuiaRoleQuery q) => new()
    {
        Size = q.PageSize,
        After = q.After,
        Before = q.Before,
        First = q.After == null && q.Before == null,
        Last = false,
    };
}

/// <summary>Adapts <see cref="KeysetPaginationResult{T}"/> to <see cref="IHuiaPageResult{T}"/>.</summary>
internal sealed class HuiaEfPageResult<T>(KeysetPaginationResult<T> inner, Func<T, string?> idSelector) : IHuiaPageResult<T>
{
    public IReadOnlyList<T> Data => inner.Data;
    public bool HasNext => inner.HasNext;
    public bool HasPrevious => inner.HasPrevious;

    // For keyset results, use the last/first item's Id as cursor tokens.
    public string? NextCursor => inner.HasNext && inner.Data.Count > 0 ? idSelector(inner.Data[^1]) : null;
    public string? PreviousCursor => inner.HasPrevious && inner.Data.Count > 0 ? idSelector(inner.Data[0]) : null;

    public int? TotalCount => inner.TotalCount > 0 ? inner.TotalCount : null;
    public int? Page => null;
}
