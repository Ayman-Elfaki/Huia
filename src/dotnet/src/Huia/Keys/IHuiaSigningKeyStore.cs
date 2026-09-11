namespace Huia.Keys;

/// <summary>The lifecycle stage of a tenant signing key.</summary>
public enum HuiaSigningKeyStatus
{
    /// <summary>Created and published in the JWKS, but not yet used for signing (propagation window).</summary>
    Pending = 0,

    /// <summary>The current signing key for its tenant. Exactly one key per tenant is active at a time.</summary>
    Active = 1,

    /// <summary>No longer signing, still published so previously issued tokens keep validating.</summary>
    Rotated = 2,

    /// <summary>Unpublished and awaiting permanent deletion after the grace period.</summary>
    Retired = 3,
}

/// <summary>Storage-agnostic snapshot of a signing key.</summary>
public sealed record HuiaSigningKeyRecord(
    string Id,
    string TenantId,
    string KeyId,
    string Algorithm,
    string PublicJwkJson,
    string? WrappedPrivateKey,
    HuiaSigningKeyStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset ActivateAt,
    DateTimeOffset? RotatedAt,
    DateTimeOffset? RetiredAt);

/// <summary>Storage abstraction for tenant signing keys.</summary>
public interface IHuiaSigningKeyStore
{
    /// <summary>Checks whether the underlying database/storage is reachable.</summary>
    ValueTask<bool> CanConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Checks whether the specified tenant has an active or pending key.</summary>
    ValueTask<bool> HasUsableKeyAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>Returns the tenant IDs that currently have an active or pending signing key.</summary>
    ValueTask<IReadOnlyList<string>> GetKeyedTenantsAsync(CancellationToken cancellationToken = default);

    /// <summary>Retrieves published keys (Pending, Active, Rotated) for the specified tenant.</summary>
    ValueTask<IReadOnlyList<HuiaSigningKeyRecord>> GetPublishedKeysAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>Retrieves all keys across all tenants (used by lifecycle background jobs).</summary>
    ValueTask<IReadOnlyList<HuiaSigningKeyRecord>> GetAllKeysAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists a newly created signing key.</summary>
    ValueTask AddKeyAsync(HuiaSigningKeyRecord key, CancellationToken cancellationToken = default);

    /// <summary>Updates the status and optional timestamp of an existing key.</summary>
    ValueTask UpdateKeyStatusAsync(string keyId, HuiaSigningKeyStatus status, DateTimeOffset? timestamp = null, CancellationToken cancellationToken = default);

    /// <summary>Deletes the specified keys permanently.</summary>
    ValueTask DeleteKeysAsync(IEnumerable<string> keyIds, CancellationToken cancellationToken = default);
}
