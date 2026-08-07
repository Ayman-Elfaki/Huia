using Huia.Keys;

namespace Huia.Tests.Unit.Keys;

/// <summary>
/// In-memory <see cref="ISigningKeyStore"/> for exercising <see cref="KeyManager"/> without a database.
/// </summary>
internal sealed class FakeSigningKeyStore(TimeProvider timeProvider) : ISigningKeyStore
{
    private readonly List<KeyDescriptor> _keys = [];
    private int _nextId;

    public IReadOnlyList<KeyDescriptor> AllKeys => _keys;

    public Task<IReadOnlyList<KeyDescriptor>> GetValidKeysAsync(KeyUsage usage,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<KeyDescriptor>>([.. _keys.Where(k => k.Usage == usage)]);

    public Task<KeyDescriptor> CreateKeyAsync(KeyUsage usage, DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        var key = new KeyDescriptor
        {
            Id = $"key-{++_nextId}",
            Usage = usage,
            CreatedAt = timeProvider.GetUtcNow(),
            ExpiresAt = expiresAt,
            Pkcs8PrivateKey = [],
        };

        _keys.Add(key);
        return Task.FromResult(key);
    }

    public Task RetireKeyAsync(string keyId, DateTimeOffset retiredAt, CancellationToken cancellationToken = default)
    {
        var index = _keys.FindIndex(k => string.Equals(k.Id, keyId, StringComparison.Ordinal));
        var existing = _keys[index];

        _keys[index] = new KeyDescriptor
        {
            Id = existing.Id,
            Usage = existing.Usage,
            CreatedAt = existing.CreatedAt,
            ExpiresAt = existing.ExpiresAt,
            RetiredAt = retiredAt,
            Pkcs8PrivateKey = existing.Pkcs8PrivateKey,
        };

        return Task.CompletedTask;
    }

    public Task PurgeExpiredKeysAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        _keys.RemoveAll(k => k.ExpiresAt < olderThan);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Test hook: directly inject a key without going through <see cref="CreateKeyAsync"/>
    /// (e.g. to backdate <see cref="KeyDescriptor.CreatedAt"/>).
    /// </summary>
    public void Seed(KeyDescriptor key) => _keys.Add(key);
}