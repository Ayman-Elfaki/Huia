namespace Huia.Headless.Services;

/// <summary>
/// Storage abstraction for revocable, rotated refresh tokens in Huia Headless.
/// Kept storage-agnostic so the headless package does not reference EF Core directly.
/// </summary>
public interface IHuiaHeadlessRefreshTokenStore
{
    /// <summary>Issues a new initial refresh token for a user session.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="userId">The user identifier.</param>
    /// <param name="lifetime">The lifetime duration of the token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A tuple containing the raw plaintext token and the created record.</returns>
    Task<(string RawToken, HuiaRefreshTokenRecord Record)> IssueAsync(
        string tenantId, string userId, TimeSpan lifetime, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rotates a refresh token: validates the presented token, marks it retired, and issues a new descendant token in the same family.
    /// If an already-rotated or revoked token is presented, detects token reuse and revokes the entire family.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="rawRefreshToken">The raw plaintext refresh token presented by the client.</param>
    /// <param name="lifetime">The lifetime duration for the replacement token.</param>
    /// <param name="slidingExpiration">Whether sliding expiration applies.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The rotation result.</returns>
    Task<RefreshTokenRotationResult> RotateAsync(
        string tenantId, string rawRefreshToken, TimeSpan lifetime, bool slidingExpiration, CancellationToken cancellationToken = default);

    /// <summary>Revokes an entire token family given any descendant token in the family.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="rawRefreshToken">The plaintext refresh token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RevokeFamilyAsync(string tenantId, string rawRefreshToken, CancellationToken cancellationToken = default);

    /// <summary>Revokes all active refresh tokens for a user within a tenant.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="userId">The user identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RevokeUserSessionsAsync(string tenantId, string userId, CancellationToken cancellationToken = default);
}
