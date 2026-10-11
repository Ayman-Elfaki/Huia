using Huia.OpenId.Entities;
using Huia.Stores;
using Microsoft.EntityFrameworkCore;
using MR.AspNetCore.Pagination;

namespace Huia.OpenId.EntityFrameworkCore;

/// <summary>
/// EF Core implementation of <see cref="IHuiaSigningKeyStore"/>. Signing keys are not tenant-filtered, so no
/// ambient tenant is needed to read or write them.
/// </summary>
internal sealed class HuiaOpenIdEfCoreSigningKeyStore<TContext>(TContext db, IPaginationService pagination)
    : IHuiaSigningKeyStore
    where TContext : DbContext
{
    public async Task<IReadOnlyList<HuiaSigningKey>> QueryAsync(
        HuiaSigningKeyFilter filter, CancellationToken cancellationToken)
    {
        var source = db.Set<HuiaSigningKey>().AsNoTracking();

        if (!string.IsNullOrEmpty(filter.TenantId))
        {
            source = source.Where(k => k.TenantId == filter.TenantId);
        }

        if (filter.Statuses is { Count: > 0 } statuses)
        {
            var list = statuses.ToList();
            source = source.Where(k => list.Contains(k.Status));
        }

        return await source.ToListAsync(cancellationToken);
    }

    public Task<HuiaSigningKey?> FindAsync(string id, CancellationToken cancellationToken)
        => db.Set<HuiaSigningKey>().AsNoTracking().FirstOrDefaultAsync(k => k.Id == id, cancellationToken);

    public async Task<IHuiaPageResult<HuiaSigningKey>> ListAsync(
        HuiaSigningKeyQuery query, CancellationToken cancellationToken)
    {
        var source = db.Set<HuiaSigningKey>().AsNoTracking();
        if (!string.IsNullOrEmpty(query.TenantId))
        {
            source = source.Where(k => k.TenantId == query.TenantId);
        }

        var result = await pagination.KeysetPaginateAsync(
            source,
            builder => builder.Ascending(k => k.Id),
            async id => await db.Set<HuiaSigningKey>().FindAsync([id], cancellationToken),
            keys => keys,
            new KeysetQueryModel
            {
                Size = query.PageSize,
                After = query.After,
                Before = query.Before,
                First = query.After == null && query.Before == null,
                Last = false,
            });

        return new HuiaEfPageResult<HuiaSigningKey>(result, k => k.Id);
    }

    public async Task SaveAsync(
        IReadOnlyCollection<HuiaSigningKey> added,
        IReadOnlyCollection<HuiaSigningKey> updated,
        IReadOnlyCollection<HuiaSigningKey> removed,
        CancellationToken cancellationToken)
    {
        if (added.Count + updated.Count + removed.Count == 0)
        {
            return;
        }

        // Rows handed to this port are detached snapshots. Start from a clean slate so an instance tracked by
        // an earlier call in the same scope cannot collide with the one being updated now.
        DetachTrackedKeys();

        var set = db.Set<HuiaSigningKey>();
        set.AddRange(added);
        set.UpdateRange(updated);
        set.RemoveRange(removed);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            DetachTrackedKeys();
        }
    }

    private void DetachTrackedKeys()
    {
        foreach (var entry in db.ChangeTracker.Entries<HuiaSigningKey>().ToList())
        {
            entry.State = EntityState.Detached;
        }
    }
}
