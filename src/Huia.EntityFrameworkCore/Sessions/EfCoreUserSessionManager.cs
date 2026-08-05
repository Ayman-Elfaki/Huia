using Huia.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Huia.EntityFrameworkCore.Sessions;

internal sealed class EfCoreUserSessionManager<TContext>(TContext context) : IUserSessionManager
    where TContext : DbContext
{
    public async Task<UserSessionDescriptor> CreateAsync(string userId, DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var entity = new UserSession
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            CreatedAt = createdAt,
            LastActivityAt = createdAt,
            ExpiresAt = expiresAt,
            IpAddress = ipAddress,
            UserAgent = userAgent,
        };

        context.Set<UserSession>().Add(entity);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToDescriptor(entity);
    }

    public async Task<UserSessionDescriptor?> FindByIdAsync(string sessionId,
        CancellationToken cancellationToken = default)
    {
        var entity = await context.Set<UserSession>().FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken)
            .ConfigureAwait(false);
        return entity is null ? null : ToDescriptor(entity);
    }

    public Task TouchAsync(string sessionId, DateTimeOffset lastActivityAt,
        CancellationToken cancellationToken = default) =>
        // A no-op (0 rows affected) if the session doesn't exist, matching the previous fetch-then-save
        // behavior — id equality is plain string comparison, so unlike the DateTimeOffset-filtered queries
        // below, this translates fine on SQLite.
        context.Set<UserSession>()
            .Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastActivityAt, lastActivityAt), cancellationToken);

    public Task RevokeAsync(string sessionId, DateTimeOffset revokedAt,
        CancellationToken cancellationToken = default) =>
        // A no-op if the session doesn't exist or is already revoked, matching the previous fetch-then-save
        // behavior — RevokedAt == null is a plain null check, not an ordering comparison, so it translates
        // fine unlike ExpiresAt's ordering comparisons in PruneAsync below.
        context.Set<UserSession>()
            .Where(s => s.Id == sessionId && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, revokedAt), cancellationToken);

    public async Task<IReadOnlyList<UserSessionDescriptor>> ListAsync(int limit, int offset, string? userId,
        CancellationToken cancellationToken = default)
    {
        var query = context.Set<UserSession>().AsQueryable();
        if (!string.IsNullOrWhiteSpace(userId))
        {
            query = query.Where(s => s.UserId == userId);
        }

        // Ordered/paged client-side rather than via OrderBy/Skip/Take's translated query: the SQLite
        // provider cannot translate DateTimeOffset comparisons in ORDER BY (see EFCoreSigningKeyStore's own
        // comment for the same underlying limitation), and this is an admin-facing listing, not a hot path.
        var entities = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
        var page = entities.OrderByDescending(s => s.LastActivityAt).Skip(offset).Take(limit);

        return [.. page.Select(ToDescriptor)];
    }

    public async Task<long> CountAsync(string? userId, CancellationToken cancellationToken = default)
    {
        var query = context.Set<UserSession>().AsQueryable();
        if (!string.IsNullOrWhiteSpace(userId))
        {
            query = query.Where(s => s.UserId == userId);
        }

        return await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task PruneAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        // Filtered client-side rather than via ExecuteDeleteAsync's translated predicate: the SQLite
        // provider cannot translate DateTimeOffset comparisons in all cases (see EFCoreSigningKeyStore's own
        // comment for the same reasoning), and this table is expected to stay small.
        var candidates = await context.Set<UserSession>().ToListAsync(cancellationToken).ConfigureAwait(false);
        var stale = candidates.Where(s => s.RevokedAt is not null || s.ExpiresAt < olderThan).ToList();

        if (stale.Count > 0)
        {
            context.Set<UserSession>().RemoveRange(stale);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static UserSessionDescriptor ToDescriptor(UserSession entity) => new()
    {
        Id = entity.Id,
        UserId = entity.UserId,
        CreatedAt = entity.CreatedAt,
        LastActivityAt = entity.LastActivityAt,
        ExpiresAt = entity.ExpiresAt,
        IpAddress = entity.IpAddress,
        UserAgent = entity.UserAgent,
        RevokedAt = entity.RevokedAt,
    };
}