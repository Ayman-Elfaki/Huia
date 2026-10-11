using Huia.OpenId.Entities;
using Huia.Options;
using Huia.Stores;
using Huia.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Huia.OpenId.EntityFrameworkCore;

/// <summary>
/// EF Core implementation of <see cref="IHuiaTenantStore"/>. Tenant rows are not tenant-filtered (they
/// describe the tenants themselves), so no ambient tenant is needed.
/// </summary>
internal sealed class HuiaOpenIdEfCoreTenantStore<TContext>(
    TContext db, HuiaTenantSerializer serializer, TimeProvider timeProvider)
    : IHuiaTenantStore
    where TContext : DbContext
{
    public async Task<IReadOnlyList<HuiaStoredTenant>> ListAsync(CancellationToken cancellationToken)
    {
        var rows = await db.Set<HuiaTenantRecord>().AsNoTracking()
            .OrderBy(t => t.TenantId)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(ToStored)];
    }

    public async Task<HuiaStoredTenant?> FindAsync(string tenantId, CancellationToken cancellationToken)
    {
        var row = await db.Set<HuiaTenantRecord>().AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId, cancellationToken);
        return row is null ? null : ToStored(row);
    }

    public async Task<HuiaStoredTenant> SaveAsync(
        string tenantId, HuiaTenant tenant, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(tenant);

        // Validate against the id it is being stored under.
        tenant.Identifier = tenantId;
        var payload = serializer.Protect(tenant);
        var now = timeProvider.GetUtcNow();

        var row = await db.Set<HuiaTenantRecord>().FirstOrDefaultAsync(t => t.TenantId == tenantId, cancellationToken);
        if (row is null)
        {
            row = new HuiaTenantRecord
            {
                TenantId = tenantId,
                Payload = payload,
                Version = 1,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.Set<HuiaTenantRecord>().Add(row);
        }
        else
        {
            row.Payload = payload;
            row.Version++;
            row.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        return new HuiaStoredTenant(tenantId, tenant, row.Version, row.UpdatedAt);
    }

    public async Task<bool> DeleteAsync(string tenantId, CancellationToken cancellationToken)
    {
        var row = await db.Set<HuiaTenantRecord>().FirstOrDefaultAsync(t => t.TenantId == tenantId, cancellationToken);
        if (row is null)
        {
            return false;
        }

        db.Set<HuiaTenantRecord>().Remove(row);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private HuiaStoredTenant ToStored(HuiaTenantRecord row) =>
        new(row.TenantId, serializer.Unprotect(row.TenantId, row.Payload), row.Version, row.UpdatedAt);
}
