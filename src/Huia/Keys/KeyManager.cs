using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace Huia.Keys;

/// <summary>
/// Manages the current signing/encryption key set, backing both <c>UseAutomaticKeyManagement()</c> and
/// <c>UseManualKeyManagement()</c>. <see cref="RotateAsync"/> drives automatic rotation: the most recently
/// created, non-retired key for a usage is always the one used to sign/encrypt new tokens; a successor is
/// generated <see cref="KeyManagementOptions.RotationLeadTime"/> ahead of the current key's retirement so
/// it is already valid (and visible via discovery) before it takes over, and a retired key stays valid for
/// validating tokens it already issued for <see cref="KeyManagementOptions.ValidationOverlap"/> past its
/// retirement. <see cref="CreateKeyAsync"/>/<see cref="RetireKeyAsync"/> instead let the app drive that
/// directly, for manual key management.
/// </summary>
/// <remarks>
/// This type is a singleton (its <see cref="Snapshot"/> is read on every request via a custom
/// <c>IConfigureOptions&lt;OpenIddictServerOptions&gt;</c>), but <see cref="ISigningKeyStore"/>
/// implementations are typically scoped around a <c>DbContext</c>. Every operation therefore resolves the
/// store from a fresh <see cref="IServiceScope"/> rather than taking it as a constructor dependency.
/// <see cref="Snapshot"/> and <see cref="GetChangeToken"/> back the change-token pipeline: OpenIddict
/// resolves <c>OpenIddictServerOptions</c> through <c>IOptionsMonitor&lt;T&gt;.CurrentValue</c> on every
/// request rather than caching it once, so cancelling the change token after a rotation makes the new key
/// set take effect immediately, without an app restart.
/// </remarks>
public sealed class KeyManager(
    IServiceScopeFactory scopeFactory,
    KeyManagementOptions options,
    TimeProvider timeProvider)
{
    private volatile KeySnapshot _snapshot = KeySnapshot.Empty;
    private CancellationTokenSource _changeTokenSource = new();

    /// <summary>The current key set. Safe to read synchronously and concurrently from any thread.</summary>
    public KeySnapshot Snapshot => _snapshot;

    /// <summary>A token that is canceled the moment <see cref="Snapshot"/> changes.</summary>
    public IChangeToken GetChangeToken() => new CancellationChangeToken(Volatile.Read(ref _changeTokenSource).Token);

    /// <summary>
    /// Ensures a current key exists for both usages, rotating/retiring as needed, then refreshes
    /// <see cref="Snapshot"/>. Safe to call repeatedly (from a startup hosted service and from the periodic
    /// Quartz job) — rotation itself is a no-op when nothing is due, but the snapshot is always refreshed.
    /// </summary>
    public async Task RotateAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISigningKeyStore>();

        await RotateUsageAsync(store, KeyUsage.Signing, cancellationToken).ConfigureAwait(false);
        await RotateUsageAsync(store, KeyUsage.Encryption, cancellationToken).ConfigureAwait(false);
        await RefreshSnapshotAsync(store, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Hard-deletes keys that are no longer valid even for token validation. Safe to call periodically.</summary>
    public async Task PurgeExpiredKeysAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISigningKeyStore>();
        await store.PurgeExpiredKeysAsync(timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a new key directly, bypassing the <see cref="RotateAsync"/> policy — for apps using
    /// <c>UseManualKeyManagement()</c> instead of <c>UseAutomaticKeyManagement()</c>, where the app itself
    /// decides when a key is created rather than <see cref="KeyManagementOptions"/>'s lead-time/lifetime
    /// policy. Refreshes <see cref="Snapshot"/> immediately afterward, so OpenIddict picks it up without an
    /// app restart.
    /// </summary>
    public async Task<KeyDescriptor> CreateKeyAsync(KeyUsage usage, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISigningKeyStore>();

        var descriptor = await store.CreateKeyAsync(usage, expiresAt, cancellationToken).ConfigureAwait(false);
        await RefreshSnapshotAsync(store, cancellationToken).ConfigureAwait(false);
        return descriptor;
    }

    /// <summary>
    /// Retires a key directly, bypassing the <see cref="RotateAsync"/> policy — see <see cref="CreateKeyAsync"/>.
    /// Refreshes <see cref="Snapshot"/> immediately afterward, so OpenIddict picks it up without an app restart.
    /// </summary>
    public async Task RetireKeyAsync(string keyId, DateTimeOffset retiredAt, CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISigningKeyStore>();

        await store.RetireKeyAsync(keyId, retiredAt, cancellationToken).ConfigureAwait(false);
        await RefreshSnapshotAsync(store, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Refreshes <see cref="Snapshot"/> from the store without creating, retiring, or purging anything —
    /// used at startup under <c>UseManualKeyManagement()</c> to pick up whatever keys already exist, without
    /// applying <see cref="RotateAsync"/>'s automatic policy.
    /// </summary>
    public async Task RefreshSnapshotAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISigningKeyStore>();
        await RefreshSnapshotAsync(store, cancellationToken).ConfigureAwait(false);
    }

    private async Task RotateUsageAsync(ISigningKeyStore store, KeyUsage usage, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var keys = await store.GetValidKeysAsync(usage, cancellationToken).ConfigureAwait(false);
        var current = CurrentKey(keys);

        if (current is null || IsDueForRotation(current, now))
        {
            var expiresAt = now + options.ActiveLifetime + options.ValidationOverlap;
            await store.CreateKeyAsync(usage, expiresAt, cancellationToken).ConfigureAwait(false);
        }

        foreach (var key in keys)
        {
            if (!key.IsRetired && key != current && HasExceededActiveLifetime(key, now))
            {
                await store.RetireKeyAsync(key.Id, now, cancellationToken).ConfigureAwait(false);
            }
        }

        // The active key may itself already be overdue (e.g. the app was down past its retirement date);
        // once its successor exists, retire it too so we never keep signing with an overdue key.
        if (current is not null && HasExceededActiveLifetime(current, now))
        {
            var refreshed = await store.GetValidKeysAsync(usage, cancellationToken).ConfigureAwait(false);
            if (CurrentKey(refreshed) != current)
            {
                await store.RetireKeyAsync(current.Id, now, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task RefreshSnapshotAsync(ISigningKeyStore store, CancellationToken cancellationToken)
    {
        var signingKeys = await store.GetValidKeysAsync(KeyUsage.Signing, cancellationToken).ConfigureAwait(false);
        var encryptionKeys = await store.GetValidKeysAsync(KeyUsage.Encryption, cancellationToken).ConfigureAwait(false);

        _snapshot = new KeySnapshot(OrderForSigning(signingKeys), OrderForSigning(encryptionKeys));

        var previous = Interlocked.Exchange(ref _changeTokenSource, new CancellationTokenSource());
        previous.Cancel();
        previous.Dispose();
    }

    private static IReadOnlyList<KeyDescriptor> OrderForSigning(IReadOnlyList<KeyDescriptor> keys)
        => [.. keys.OrderByDescending(k => !k.IsRetired).ThenByDescending(k => k.CreatedAt)];

    private static KeyDescriptor? CurrentKey(IReadOnlyList<KeyDescriptor> keys)
        => keys.Where(k => !k.IsRetired).OrderByDescending(k => k.CreatedAt).FirstOrDefault();

    private bool IsDueForRotation(KeyDescriptor key, DateTimeOffset now)
        => now >= key.CreatedAt + options.ActiveLifetime - options.RotationLeadTime;

    private bool HasExceededActiveLifetime(KeyDescriptor key, DateTimeOffset now)
        => now >= key.CreatedAt + options.ActiveLifetime;
}