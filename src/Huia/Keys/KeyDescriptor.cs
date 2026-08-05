namespace Huia.Keys;

/// <summary>
/// A store-agnostic view of one managed RSA key. <see cref="Pkcs8PrivateKey"/> is the plaintext,
/// already-decrypted private key material — callers must not persist it themselves; the store is the only
/// place it is written to disk/DB, encrypted at rest.
/// </summary>
public sealed class KeyDescriptor
{
    /// <summary>Unique identifier for this key, used as the JWT/JWK <c>kid</c>.</summary>
    public required string Id { get; init; }

    /// <summary>Whether this key is used for signing or encrypting tokens.</summary>
    public required KeyUsage Usage { get; init; }

    /// <summary>When this key was generated.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>When this key stops being valid even for validating previously issued tokens.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>
    /// When this key was retired from signing new tokens. <c>null</c> means it is still the active key for
    /// its <see cref="Usage"/>. A retired key remains valid for validation until <see cref="ExpiresAt"/>.
    /// </summary>
    public DateTimeOffset? RetiredAt { get; init; }

    /// <summary>The RSA private key, PKCS#8 DER-encoded, decrypted.</summary>
    public required byte[] Pkcs8PrivateKey { get; init; }

    /// <summary>Whether this key has been retired from signing/encrypting new tokens.</summary>
    public bool IsRetired => RetiredAt is not null;
}