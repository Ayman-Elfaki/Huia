using Microsoft.IdentityModel.Tokens;

namespace Huia.Keys;

/// <summary>
/// Per-tenant view of the signing keys. Backed by <c>HybridCache</c> for stampede protection and
/// L1/L2 coherency; call <see cref="InvalidateAsync"/> after any lifecycle change.
/// </summary>
public interface IHuiaKeyRing
{
    /// <summary>The key currently used to sign tokens for a tenant.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The active key, including private material.</returns>
    /// <exception cref="InvalidOperationException">The tenant has no active key.</exception>
    ValueTask<HuiaSigningKeyMaterial> GetActiveSigningKeyAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>Every published key for a tenant (pending, active and rotated) as validation keys.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The validation keys, newest first.</returns>
    ValueTask<IReadOnlyList<HuiaSigningKeyMaterial>> GetPublishedKeysAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>The public JWKs advertised in a tenant's JSON Web Key Set.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The public JWKs.</returns>
    ValueTask<IReadOnlyList<JsonWebKey>> GetPublishedJwksAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>Drops the cached view for a tenant (after rotation, activation, retirement or deletion).</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <returns>A task that completes when the cache entry is gone.</returns>
    ValueTask InvalidateAsync(string tenantId);
}
