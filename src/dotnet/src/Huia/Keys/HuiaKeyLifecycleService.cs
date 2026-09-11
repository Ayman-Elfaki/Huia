using Huia.Options;
using Microsoft.Extensions.Logging;

namespace Huia.Keys;

/// <summary>
/// The signing-key state machine, driven by the Quartz jobs (and once at start-up by the bootstrapper).
/// All <see cref="DateTimeOffset"/> comparisons are done in memory after a status-only query so the same
/// code runs on all storage providers.
/// </summary>
internal sealed partial class HuiaKeyLifecycleService(
    IHuiaSigningKeyStore keyStore,
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
            var hasUsableKey = await keyStore.HasUsableKeyAsync(tenantId, cancellationToken);
            if (hasUsableKey)
            {
                continue;
            }

            var now = timeProvider.GetUtcNow();
            var key = keyFactory.Create(tenantId, options.Keys, HuiaSigningKeyStatus.Active, now);
            await keyStore.AddKeyAsync(key, cancellationToken);
            await keyRing.InvalidateAsync(tenantId);
            LogBootstrapped(tenantId, key.KeyId);
        }
    }

    /// <summary>Creates a pending successor for any tenant whose active key is older than the rotation interval.</summary>
    public async Task RotateAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var allKeys = await keyStore.GetAllKeysAsync(cancellationToken);

        foreach (var tenantId in options.Tenants.Keys)
        {
            var keys = allKeys.Where(k => k.TenantId == tenantId).ToList();

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
            await keyStore.AddKeyAsync(successor, cancellationToken);
            await keyRing.InvalidateAsync(tenantId);
            LogRotated(tenantId, successor.KeyId, successor.ActivateAt);
        }
    }

    /// <summary>Promotes pending keys that have reached their activation time and demotes the previous active key.</summary>
    public async Task ActivateDueAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var allKeys = await keyStore.GetAllKeysAsync(cancellationToken);

        var pending = allKeys.Where(k => k.Status == HuiaSigningKeyStatus.Pending).ToList();

        foreach (var group in pending.Where(k => k.ActivateAt <= now).GroupBy(k => k.TenantId))
        {
            var promote = group.OrderBy(k => k.ActivateAt).First();

            var currentActive = allKeys
                .Where(k => k.TenantId == group.Key && k.Status == HuiaSigningKeyStatus.Active)
                .ToList();

            foreach (var key in currentActive)
            {
                await keyStore.UpdateKeyStatusAsync(key.KeyId, HuiaSigningKeyStatus.Rotated, now, cancellationToken);
            }

            await keyStore.UpdateKeyStatusAsync(promote.KeyId, HuiaSigningKeyStatus.Active, null, cancellationToken);
            await keyRing.InvalidateAsync(group.Key);
            LogActivated(group.Key, promote.KeyId);
        }
    }

    /// <summary>Unpublishes rotated keys once the retention period has elapsed.</summary>
    public async Task RetireAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var allKeys = await keyStore.GetAllKeysAsync(cancellationToken);

        var rotated = allKeys.Where(k => k.Status == HuiaSigningKeyStatus.Rotated).ToList();

        foreach (var key in rotated.Where(k => k.RotatedAt is { } r && now - r >= options.Keys.RetentionPeriod))
        {
            await keyStore.UpdateKeyStatusAsync(key.KeyId, HuiaSigningKeyStatus.Retired, now, cancellationToken);
            await keyRing.InvalidateAsync(key.TenantId);
            LogRetired(key.TenantId, key.KeyId);
        }
    }

    /// <summary>Permanently deletes retired keys after the grace period.</summary>
    public async Task DeleteAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var allKeys = await keyStore.GetAllKeysAsync(cancellationToken);

        var retired = allKeys.Where(k => k.Status == HuiaSigningKeyStatus.Retired).ToList();

        var expired = retired
            .Where(k => k.RetiredAt is { } r && now - r >= options.Keys.RetiredKeyGracePeriod)
            .ToList();

        if (expired.Count == 0)
        {
            return;
        }

        await keyStore.DeleteKeysAsync(expired.Select(k => k.Id), cancellationToken);
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
