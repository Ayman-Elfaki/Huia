namespace Huia.Keys;

/// <summary>
/// Persists Huia's managed signing/encryption keys, under either <c>huia.KeysManagement.UseAutomaticKeyManagement()</c>
/// or <c>UseManualKeyManagement()</c>. Implemented against EF Core by <c>Huia.EntityFrameworkCore</c>; private
/// key material must be encrypted at rest by the implementation.
/// </summary>
/// <remarks>
/// This is the provider-agnostic extension point for key management — it lives in core <c>Huia</c> and has
/// no dependency on EF Core. To back key storage with something other than EF Core (e.g. MongoDB, Redis, a
/// cloud KMS), implement this interface directly and register it as <see cref="ISigningKeyStore"/>
/// instead of calling <c>WithEntityFrameworkStores</c>.
/// </remarks>
public interface ISigningKeyStore
{
    /// <summary>
    /// All keys for <paramref name="usage"/> that are still valid for token validation — i.e. not yet past
    /// <see cref="KeyDescriptor.ExpiresAt"/> — including retired ones. The caller determines which one
    /// (if any) is the current signing key by picking the non-retired one.
    /// </summary>
    Task<IReadOnlyList<KeyDescriptor>> GetValidKeysAsync(KeyUsage usage, CancellationToken cancellationToken = default);

    /// <summary>Generates a new RSA key, persists it, and returns its descriptor.</summary>
    Task<KeyDescriptor> CreateKeyAsync(KeyUsage usage, DateTimeOffset expiresAt, CancellationToken cancellationToken = default);

    /// <summary>Marks a key retired so it stops being used to sign new tokens but stays valid for validation.</summary>
    Task RetireKeyAsync(string keyId, DateTimeOffset retiredAt, CancellationToken cancellationToken = default);

    /// <summary>Hard-deletes keys that expired before <paramref name="olderThan"/>.</summary>
    Task PurgeExpiredKeysAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default);
}