namespace Huia.Sessions;

/// <summary>
/// signed-in user session — created once per interactive sign-in (password, 2FA, registration, or device-flow approval)
/// and referenced by every OpenIddict authorization/token minted afterward via the <c>sid</c> claim,
/// until it's revoked or its <see cref="ExpiresAt"/> passes.
/// </summary>
public sealed class UserSessionDescriptor
{
    /// <summary>
    /// Unique identifier for this session — the value carried on tokens as the <c>sid</c> claim.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The signed-in user's id (matches <c>sub</c>).
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// When the user signed in and this session was created.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Last time this session was used to mint a token or SSO into another client — updated on every
    /// <c>/connect/authorize</c>, <c>/connect/token</c>, or <c>/connect/verify</c> hit that resolves to this
    /// session.
    /// </summary>
    public required DateTimeOffset LastActivityAt { get; init; }

    /// <summary>
    /// Absolute expiry, independent of activity — see <c>HuiaUserSessionsOptions.AbsoluteLifetime</c>.
    /// </summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>
    /// Best-effort, from the request that created this session. May be <see langword="null"/>.
    /// </summary>
    public string? IpAddress { get; init; }

    /// <summary>
    /// Best-effort, from the request that created this session. May be <see langword="null"/>.
    /// </summary>
    public string? UserAgent { get; init; }

    /// <summary>
    /// When this session was explicitly revoked (sign-out or admin action); <see langword="null"/> while live.
    /// </summary>
    public DateTimeOffset? RevokedAt { get; init; }

    /// <summary>
    /// Whether this session is still usable — not revoked and not past <see cref="ExpiresAt"/>.
    /// </summary>
    public bool IsLive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}