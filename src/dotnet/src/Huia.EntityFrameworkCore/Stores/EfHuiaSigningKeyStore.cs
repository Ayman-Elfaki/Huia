using Huia.EntityFrameworkCore.Entities;
using Huia.Keys;
using Microsoft.EntityFrameworkCore;

namespace Huia.EntityFrameworkCore.Stores;

/// <summary>EF Core implementation of <see cref="IHuiaSigningKeyStore"/> backed by <see cref="HuiaDbContext.SigningKeys"/>.</summary>
public class EfHuiaSigningKeyStore(HuiaDbContext dbContext) : IHuiaSigningKeyStore
{
    /// <inheritdoc />
    public async ValueTask<bool> CanConnectAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.Database.CanConnectAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<bool> HasUsableKeyAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        return await dbContext.SigningKeys.AnyAsync(
            k => k.TenantId == tenantId &&
                 (k.Status == HuiaSigningKeyStatus.Active || k.Status == HuiaSigningKeyStatus.Pending),
            cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<string>> GetKeyedTenantsAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.SigningKeys
            .Where(k => k.Status == HuiaSigningKeyStatus.Active || k.Status == HuiaSigningKeyStatus.Pending)
            .Select(k => k.TenantId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<HuiaSigningKeyRecord>> GetPublishedKeysAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.SigningKeys.AsNoTracking()
            .Where(k => k.TenantId == tenantId &&
                        (k.Status == HuiaSigningKeyStatus.Pending ||
                         k.Status == HuiaSigningKeyStatus.Active ||
                         k.Status == HuiaSigningKeyStatus.Rotated))
            .ToListAsync(cancellationToken);

        return rows.OrderByDescending(k => k.CreatedAt)
            .Select(ToRecord)
            .ToList();
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<HuiaSigningKeyRecord>> GetAllKeysAsync(CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.SigningKeys.AsNoTracking().ToListAsync(cancellationToken);
        return rows.Select(ToRecord).ToList();
    }

    /// <inheritdoc />
    public async ValueTask AddKeyAsync(HuiaSigningKeyRecord key, CancellationToken cancellationToken = default)
    {
        var entity = new HuiaSigningKey
        {
            Id = key.Id,
            TenantId = key.TenantId,
            KeyId = key.KeyId,
            Algorithm = key.Algorithm,
            PublicJwkJson = key.PublicJwkJson,
            WrappedPrivateKey = key.WrappedPrivateKey ?? string.Empty,
            Status = key.Status,
            CreatedAt = key.CreatedAt,
            ActivateAt = key.ActivateAt,
            RotatedAt = key.RotatedAt,
            RetiredAt = key.RetiredAt,
        };
        dbContext.SigningKeys.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask UpdateKeyStatusAsync(string keyId, HuiaSigningKeyStatus status, DateTimeOffset? timestamp = null, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.SigningKeys.FirstOrDefaultAsync(k => k.KeyId == keyId || k.Id == keyId, cancellationToken);
        if (entity is not null)
        {
            entity.Status = status;
            if (status == HuiaSigningKeyStatus.Rotated)
            {
                entity.RotatedAt = timestamp ?? DateTimeOffset.UtcNow;
            }
            else if (status == HuiaSigningKeyStatus.Retired)
            {
                entity.RetiredAt = timestamp ?? DateTimeOffset.UtcNow;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    /// <inheritdoc />
    public async ValueTask DeleteKeysAsync(IEnumerable<string> keyIds, CancellationToken cancellationToken = default)
    {
        var idList = keyIds.ToList();
        var keys = await dbContext.SigningKeys.Where(k => idList.Contains(k.Id) || idList.Contains(k.KeyId)).ToListAsync(cancellationToken);
        if (keys.Count > 0)
        {
            dbContext.SigningKeys.RemoveRange(keys);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static HuiaSigningKeyRecord ToRecord(HuiaSigningKey k) =>
        new(k.Id, k.TenantId, k.KeyId, k.Algorithm, k.PublicJwkJson, k.WrappedPrivateKey,
            (Huia.Keys.HuiaSigningKeyStatus)k.Status, k.CreatedAt, k.ActivateAt, k.RotatedAt, k.RetiredAt);
}
