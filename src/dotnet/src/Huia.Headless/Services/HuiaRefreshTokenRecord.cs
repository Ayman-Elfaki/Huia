namespace Huia.Headless.Services;

/// <summary>Represents a persisted refresh token in the headless store.</summary>
public sealed record HuiaRefreshTokenRecord
{
    /// <summary>Unique identifier of the token record.</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>Owning tenant identifier.</summary>
    public string TenantId { get; init; } = string.Empty;

    /// <summary>User the token belongs to.</summary>
    public string UserId { get; init; } = string.Empty;

    /// <summary>SHA-256 hash of the raw token string.</summary>
    public string TokenHash { get; init; } = string.Empty;

    /// <summary>Shared identifier for all rotated tokens descending from an initial login session.</summary>
    public string FamilyId { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>When the token was issued.</summary>
    public DateTimeOffset IssuedAt { get; init; }

    /// <summary>When the token expires.</summary>
    public DateTimeOffset ExpiresAt { get; init; }

    /// <summary>When the token was revoked, or null if active.</summary>
    public DateTimeOffset? RevokedAt { get; init; }

    /// <summary>SHA-256 hash of the replacement token when rotated.</summary>
    public string? ReplacedByTokenHash { get; init; }

    /// <summary>Whether this token is active (not revoked and not expired).</summary>
    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}

/// <summary>Status of a refresh token rotation attempt.</summary>
public enum RefreshTokenRotationStatus
{
    /// <summary>Successfully rotated: old token retired, new token issued.</summary>
    Success,

    /// <summary>The token was already revoked, replaced, or reused — entire family revoked.</summary>
    Revoked,

    /// <summary>The token has passed its expiration timestamp.</summary>
    Expired,

    /// <summary>The token hash was not found.</summary>
    NotFound,
}

/// <summary>The outcome of a refresh token rotation operation.</summary>
/// <param name="Status">The outcome status.</param>
/// <param name="NewRawRefreshToken">The new plaintext refresh token, if successful.</param>
/// <param name="UserId">The user identifier associated with the token session.</param>
/// <param name="ExpiresIn">The lifespan of the newly issued token.</param>
public readonly record struct RefreshTokenRotationResult(
    RefreshTokenRotationStatus Status,
    string? NewRawRefreshToken,
    string? UserId,
    TimeSpan? ExpiresIn)
{
    /// <summary>Whether rotation succeeded.</summary>
    public bool Succeeded => Status == RefreshTokenRotationStatus.Success;
}
