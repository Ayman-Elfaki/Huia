using Huia.EntityFrameworkCore;
using Huia.EntityFrameworkCore.Entities;
using Huia.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Huia.AspNetCore.Keys;

/// <summary>
/// The signing-key state machine, driven by the Quartz jobs (and once at start-up by the bootstrapper).
/// All <see cref="DateTimeOffset"/> comparisons are done in memory after a status-only query so the same
/// code runs on SQLite, which cannot translate <see cref="DateTimeOffset"/> comparisons.
/// </summary>
internal sealed partial class HuiaKeyLifecycleService(
    HuiaDbContext dbContext,
    HuiaSigningKeyFactory keyFactory,
    IHuiaKeyRing keyRing,
    HuiaOptions options,
    TimeProvider timeProvider,
    ILogger<HuiaKeyLifecycleService> logger)
{
    /// <summary>Guarantees every configured tenant has a usable signing key. Run once at start-up.</summary>
    public async Task EnsureBootstrappedAsync(CancellationToken cancellationToken)
    {
        foreach (var tenantId in options.Tenants.Keys)
        {
            var hasUsableKey = await dbContext.SigningKeys.AnyAsync(
                k => k.TenantId == tenantId &&
                     (k.Status == HuiaSigningKeyStatus.Active || k.Status == HuiaSigningKeyStatus.Pending),
                cancellationToken);

            if (hasUsableKey)
            {
                continue;
            }

            var now = timeProvider.GetUtcNow();
            var key = keyFactory.Create(tenantId, options.Keys, HuiaSigningKeyStatus.Active, now);
            dbContext.SigningKeys.Add(key);
            await dbContext.SaveChangesAsync(cancellationToken);
            await keyRing.InvalidateAsync(tenantId);
            LogBootstrapped(tenantId, key.KeyId);
        }
    }

    /// <summary>Creates a pending successor for any tenant whose active key is older than the rotation interval.</summary>
    public async Task RotateAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        foreach (var tenantId in options.Tenants.Keys)
        {
            var keys = await dbContext.SigningKeys
                .Where(k => k.TenantId == tenantId)
                .ToListAsync(cancellationToken);

            if (keys.Any(k => k.Status == HuiaSigningKeyStatus.Pending))
            {
                continue;
            }

            var active = keys
                .Where(k => k.Status == HuiaSigningKeyStatus.Active)
                .OrderByDescending(k => k.CreatedAt)
                .FirstOrDefault();

            if (active is null || now - active.CreatedAt < options.Keys.RotationInterval)
            {
                continue;
            }

            var successor = keyFactory.Create(tenantId, options.Keys, HuiaSigningKeyStatus.Pending, now + options.Keys.ActivationDelay);
            dbContext.SigningKeys.Add(successor);
            await dbContext.SaveChangesAsync(cancellationToken);
            await keyRing.InvalidateAsync(tenantId);
            LogRotated(tenantId, successor.KeyId, successor.ActivateAt);
        }
    }

    /// <summary>Promotes pending keys that have reached their activation time and demotes the previous active key.</summary>
    public async Task ActivateDueAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        var pending = await dbContext.SigningKeys
            .Where(k => k.Status == HuiaSigningKeyStatus.Pending)
            .ToListAsync(cancellationToken);

        foreach (var group in pending.Where(k => k.ActivateAt <= now).GroupBy(k => k.TenantId))
        {
            var promote = group.OrderBy(k => k.ActivateAt).First();

            var currentActive = await dbContext.SigningKeys
                .Where(k => k.TenantId == group.Key && k.Status == HuiaSigningKeyStatus.Active)
                .ToListAsync(cancellationToken);

            foreach (var key in currentActive)
            {
                key.Status = HuiaSigningKeyStatus.Rotated;
                key.RotatedAt = now;
            }

            promote.Status = HuiaSigningKeyStatus.Active;
            await dbContext.SaveChangesAsync(cancellationToken);
            await keyRing.InvalidateAsync(group.Key);
            LogActivated(group.Key, promote.KeyId);
        }
    }

    /// <summary>Unpublishes rotated keys once the retention period has elapsed.</summary>
    public async Task RetireAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        var rotated = await dbContext.SigningKeys
            .Where(k => k.Status == HuiaSigningKeyStatus.Rotated)
            .ToListAsync(cancellationToken);

        foreach (var key in rotated.Where(k => k.RotatedAt is { } r && now - r >= options.Keys.RetentionPeriod))
        {
            key.Status = HuiaSigningKeyStatus.Retired;
            key.RetiredAt = now;
            await keyRing.InvalidateAsync(key.TenantId);
            LogRetired(key.TenantId, key.KeyId);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Permanently deletes retired keys after the grace period.</summary>
    public async Task DeleteAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        var retired = await dbContext.SigningKeys
            .Where(k => k.Status == HuiaSigningKeyStatus.Retired)
            .ToListAsync(cancellationToken);

        var expired = retired
            .Where(k => k.RetiredAt is { } r && now - r >= options.Keys.RetiredKeyGracePeriod)
            .ToList();

        if (expired.Count == 0)
        {
            return;
        }

        dbContext.SigningKeys.RemoveRange(expired);
        await dbContext.SaveChangesAsync(cancellationToken);
        foreach (var tenantId in expired.Select(k => k.TenantId).Distinct())
        {
            await keyRing.InvalidateAsync(tenantId);
        }

        LogDeleted(expired.Count);
    }

    [LoggerMessage(LogLevel.Information, "Bootstrapped signing key {KeyId} for tenant {TenantId}.")]
    partial void LogBootstrapped(string tenantId, string keyId);

    [LoggerMessage(LogLevel.Information, "Rotated tenant {TenantId}: pending key {KeyId} activates at {ActivateAt:o}.")]
    partial void LogRotated(string tenantId, string keyId, DateTimeOffset activateAt);

    [LoggerMessage(LogLevel.Information, "Activated signing key {KeyId} for tenant {TenantId}.")]
    partial void LogActivated(string tenantId, string keyId);

    [LoggerMessage(LogLevel.Information, "Retired signing key {KeyId} for tenant {TenantId}.")]
    partial void LogRetired(string tenantId, string keyId);

    [LoggerMessage(LogLevel.Information, "Deleted {Count} expired signing key(s).")]
    partial void LogDeleted(int count);
}
