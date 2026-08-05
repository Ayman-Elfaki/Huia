namespace Huia.Sessions;

/// <summary>
/// Persists Huia's user sessions. Implemented against EF Core by <c>Huia.EntityFrameworkCore</c>.
/// </summary>
/// <remarks>
/// This is the provider-agnostic extension point for session storage — it lives in core <c>Huia</c> and has
/// no dependency on EF Core. To back it with something other than EF Core (e.g. Redis, MongoDB), implement
/// this interface directly and register it as <see cref="IUserSessionManager"/> instead of calling
/// <c>WithEntityFrameworkStores</c>.
/// </remarks>
public interface IUserSessionManager
{
    /// <summary>
    /// Creates and persists a new session.
    /// </summary>
    Task<UserSessionDescriptor> CreateAsync(string userId, DateTimeOffset createdAt, DateTimeOffset expiresAt,
        string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up a session by id, or <see langword="null"/> if it doesn't exist.
    /// </summary>
    Task<UserSessionDescriptor?> FindByIdAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bumps <see cref="UserSessionDescriptor.LastActivityAt"/> — see its own doc comment for when this is called.
    /// </summary>
    Task TouchAsync(string sessionId, DateTimeOffset lastActivityAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a session revoked. A no-op if it doesn't exist or is already revoked.
    /// </summary>
    Task RevokeAsync(string sessionId, DateTimeOffset revokedAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists sessions, most recently active first, optionally filtered to one user. Used by the admin
    /// <c>/sessions</c> endpoints.
    /// </summary>
    Task<IReadOnlyList<UserSessionDescriptor>> ListAsync(int limit, int offset, string? userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Total count matching the same <paramref name="userId"/> filter <see cref="ListAsync"/> would use, for paging.
    /// </summary>
    Task<long> CountAsync(string? userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hard-deletes sessions that are revoked or past <see cref="UserSessionDescriptor.ExpiresAt"/> as of <paramref name="olderThan"/>.
    /// </summary>
    Task PruneAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default);
}