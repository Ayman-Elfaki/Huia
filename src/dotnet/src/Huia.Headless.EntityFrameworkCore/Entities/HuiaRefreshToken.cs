namespace Huia.Headless.EntityFrameworkCore.Entities;

/// <summary>
/// Persisted refresh token record supporting revocation and rotation.
/// </summary>
public class HuiaRefreshToken
{
    /// <summary>Primary key for the token record.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Owning tenant identifier.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>User identifier the token belongs to.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>SHA-256 hash of the raw token string (raw token is never stored).</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Shared identifier for all rotated tokens descending from a single sign-in session.</summary>
    public string FamilyId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>When the token was issued (UTC).</summary>
    public DateTimeOffset IssuedAt { get; set; }

    /// <summary>When the token expires (UTC).</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>When the token was revoked or retired during rotation (UTC), or null if active.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>SHA-256 hash of the replacement token issued during rotation.</summary>
    public string? ReplacedByTokenHash { get; set; }

    /// <summary>Whether this token is active at the specified time.</summary>
    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}
