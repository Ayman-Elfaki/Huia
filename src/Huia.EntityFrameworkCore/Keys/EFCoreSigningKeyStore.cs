using System.Security.Cryptography;
using Huia.Keys;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Huia.EntityFrameworkCore.Keys;

internal sealed class EfCoreSigningKeyStore<TContext>(
    TContext context,
    IDataProtectionProvider dataProtectionProvider,
    KeyManagementOptions options,
    TimeProvider timeProvider,
    ILogger<EfCoreSigningKeyStore<TContext>> logger) : ISigningKeyStore
    where TContext : DbContext
{
    private readonly IDataProtector _protector =
        dataProtectionProvider.CreateProtector("Huia.Keys.SigningKeys.v1");

    public async Task<IReadOnlyList<KeyDescriptor>> GetValidKeysAsync(KeyUsage usage,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();

        // ExpiresAt is filtered client-side: the SQLite provider cannot translate DateTimeOffset comparisons
        // in all cases, and this table only ever holds a handful of rows per usage, so it's a non-issue.
        var entities = await context.Set<SigningKey>()
            .Where(k => k.Usage == usage)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var descriptors = new List<KeyDescriptor>(entities.Count);
        foreach (var entity in entities.Where(k => k.ExpiresAt > now))
        {
            try
            {
                descriptors.Add(ToDescriptor(entity));
            }
            catch (CryptographicException ex)
            {
                // The Data Protection key ring that encrypted this key is no longer available (e.g. the app
                // moved/was renamed and its key-isolation discriminator changed, or the key ring was deleted
                // externally). Treat it as absent rather than crashing the whole host — HuiaKeyManager mints a
                // replacement when no current key exists.
                logger.LogWarning(ex, "Signing key {KeyId} ({Usage}) could not be decrypted; treating it as absent.",
                    entity.Id, entity.Usage);
            }
        }

        return descriptors;
    }

    public async Task<KeyDescriptor> CreateKeyAsync(KeyUsage usage, DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        using var rsa = RSA.Create(options.RsaKeySizeInBits);

        var entity = new SigningKey
        {
            Id = Guid.NewGuid().ToString("N"),
            Usage = usage,
            CreatedAt = timeProvider.GetUtcNow(),
            ExpiresAt = expiresAt,
            ProtectedPrivateKey = _protector.Protect(rsa.ExportPkcs8PrivateKey()),
        };

        context.Set<SigningKey>().Add(entity);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToDescriptor(entity);
    }

    public async Task RetireKeyAsync(string keyId, DateTimeOffset retiredAt,
        CancellationToken cancellationToken = default)
    {
        // Id equality is plain string comparison, so unlike the DateTimeOffset-filtered queries below, this
        // translates fine on SQLite. FirstAsync's throw-if-missing behavior is preserved manually since
        // ExecuteUpdateAsync just reports 0 rows affected instead of throwing.
        var updated = await context.Set<SigningKey>()
            .Where(k => k.Id == keyId)
            .ExecuteUpdateAsync(s => s.SetProperty(k => k.RetiredAt, retiredAt), cancellationToken)
            .ConfigureAwait(false);

        if (updated == 0)
        {
            throw new InvalidOperationException($"Signing key '{keyId}' was not found.");
        }
    }

    public async Task PurgeExpiredKeysAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        // See the comment in GetValidKeysAsync: ExpiresAt is filtered client-side rather than via
        // ExecuteDeleteAsync's translated predicate.
        var expired = await context.Set<SigningKey>().ToListAsync(cancellationToken).ConfigureAwait(false);
        var toRemove = expired.Where(k => k.ExpiresAt < olderThan).ToList();

        if (toRemove.Count > 0)
        {
            context.Set<SigningKey>().RemoveRange(toRemove);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private KeyDescriptor ToDescriptor(SigningKey entity) => new()
    {
        Id = entity.Id,
        Usage = entity.Usage,
        CreatedAt = entity.CreatedAt,
        ExpiresAt = entity.ExpiresAt,
        RetiredAt = entity.RetiredAt,
        Pkcs8PrivateKey = _protector.Unprotect(entity.ProtectedPrivateKey),
    };
}