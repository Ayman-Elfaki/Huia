using Huia.Entities;
using Huia.Stores;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MR.AspNetCore.Pagination;

namespace Huia.Headless.EntityFrameworkCore;

/// <summary>
/// EF Core implementation of <see cref="IHuiaStore{TUser,TRole}"/> for the single-tenant
/// <c>Huia.Headless</c> flavor. Supports both keyset pagination (when <c>After</c>/<c>Before</c>
/// cursors are supplied) and offset pagination (for simple page-based clients).
/// </summary>
internal sealed class HuiaHeadlessEfCoreStore<TContext, TUser, TRole>(
    TContext db,
    IPaginationService pagination)
    : IHuiaStore<TUser, TRole>
    where TContext : DbContext
    where TUser : HuiaUser
    where TRole : HuiaRole
{
    public async Task<IHuiaPageResult<TUser>> ListUsersAsync(HuiaUserQuery query, CancellationToken ct)
    {
        var source = db.Set<TUser>().AsNoTracking();

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
            var matchingUserIds = db.Set<IdentityUserRole<string>>().AsNoTracking()
                .Join(
                    db.Set<TRole>().AsNoTracking().Where(r => roles.Contains(r.Name!)),
                    ur => ur.RoleId,
                    r => r.Id,
                    (ur, r) => ur.UserId);

            source = source.Where(u => matchingUserIds.Contains(u.Id));
        }

        if (query.After != null || query.Before != null)
        {
            // Keyset pagination — cursor-based, O(log n), no row count.
            var result = await pagination.KeysetPaginateAsync(
                source,
                builder => builder.Ascending(u => u.Id),
                async id => await db.Set<TUser>().FirstOrDefaultAsync(u => u.Id == id, ct),
                users => users,
                new KeysetQueryModel
                {
                    Size = query.PageSize,
                    After = query.After,
                    Before = query.Before,
                    First = query.After == null && query.Before == null,
                    Last = false,
                });

            return new HuiaKeysetPageResult<TUser>(result, u => u.Id);
        }
        else
        {
            // Offset pagination — for clients that need total counts and page numbers.
            var totalCount = await source.CountAsync(ct);
            var page = Math.Max(1, query.Page);
            var data = await source
                .OrderBy(u => u.Id)
                .Skip((page - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync(ct);

            return new HuiaOffsetPageResult<TUser>(data, totalCount, page, query.PageSize);
        }
    }

    public async Task<IHuiaPageResult<TRole>> ListRolesAsync(HuiaRoleQuery query, CancellationToken ct)
    {
        var source = db.Set<TRole>().AsNoTracking();

        if (query.After != null || query.Before != null)
        {
            var result = await pagination.KeysetPaginateAsync(
                source,
                builder => builder.Ascending(r => r.Id),
                async id => await db.Set<TRole>().FirstOrDefaultAsync(r => r.Id == id, ct),
                roles => roles,
                new KeysetQueryModel
                {
                    Size = query.PageSize,
                    After = query.After,
                    Before = query.Before,
                    First = query.After == null && query.Before == null,
                    Last = false,
                });

            return new HuiaKeysetPageResult<TRole>(result, r => r.Id);
        }
        else
        {
            var totalCount = await source.CountAsync(ct);
            var page = Math.Max(1, query.Page);
            var data = await source
                .OrderBy(r => r.Id)
                .Skip((page - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync(ct);

            return new HuiaOffsetPageResult<TRole>(data, totalCount, page, query.PageSize);
        }
    }

    public async Task<IReadOnlyDictionary<string, string[]>> GetRolesByUserIdsAsync(
        IReadOnlyCollection<string> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<string, string[]>();
        }

        var rows = await db.Set<IdentityUserRole<string>>().AsNoTracking()
            .Where(ur => userIds.Contains(ur.UserId))
            .Join(db.Set<TRole>().AsNoTracking(),
                ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .ToListAsync(ct);

        return rows
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Name!).ToArray());
    }
}

/// <summary>Wraps a <see cref="KeysetPaginationResult{T}"/> as <see cref="IHuiaPageResult{T}"/>.</summary>
internal sealed class HuiaKeysetPageResult<T>(KeysetPaginationResult<T> inner, Func<T, string?> idSelector) : IHuiaPageResult<T>
{
    public IReadOnlyList<T> Data => inner.Data;
    public bool HasNext => inner.HasNext;
    public bool HasPrevious => inner.HasPrevious;
    public string? NextCursor => inner.HasNext && inner.Data.Count > 0 ? idSelector(inner.Data[^1]) : null;
    public string? PreviousCursor => inner.HasPrevious && inner.Data.Count > 0 ? idSelector(inner.Data[0]) : null;
    public int? TotalCount => inner.TotalCount > 0 ? inner.TotalCount : null;
    public int? Page => null;
}

/// <summary>Offset page result with a total count and page number.</summary>
internal sealed class HuiaOffsetPageResult<T>(IReadOnlyList<T> data, int totalCount, int page, int pageSize)
    : IHuiaPageResult<T>
{
    public IReadOnlyList<T> Data => data;
    public bool HasNext => page * pageSize < totalCount;
    public bool HasPrevious => page > 1;
    public string? NextCursor => null;
    public string? PreviousCursor => null;
    public int? TotalCount => totalCount;
    public int? Page => page;
}
