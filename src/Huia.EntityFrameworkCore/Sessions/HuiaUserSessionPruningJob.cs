using Microsoft.EntityFrameworkCore;
using Quartz;

namespace Huia.EntityFrameworkCore.Sessions;

/// <summary>Quartz job that hard-deletes revoked/expired <see cref="UserSession"/> rows.</summary>
[DisallowConcurrentExecution]
internal sealed class HuiaUserSessionPruningJob<TContext>(TContext dbContext, TimeProvider timeProvider) : IJob
    where TContext : DbContext
{
    public async Task Execute(IJobExecutionContext jobContext)
    {
        var candidates = await dbContext.Set<UserSession>().ToListAsync(jobContext.CancellationToken)
            .ConfigureAwait(false);

        var now = timeProvider.GetUtcNow();
        var stale = candidates.Where(s => s.RevokedAt is not null || s.ExpiresAt < now).ToList();

        if (stale.Count > 0)
        {
            dbContext.Set<UserSession>().RemoveRange(stale);
            await dbContext.SaveChangesAsync(jobContext.CancellationToken).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// EF Core row backing one <c>Huia.Sessions.HuiaUserSessionDescriptor</c>.
/// </summary>
public class UserSession
{
    /// <summary>
    /// Unique identifier for this session — the value carried on tokens as the <c>sid</c> claim.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// The signed-in user's id (matches <c>sub</c>).
    /// </summary>
    public required string UserId { get; set; }

    /// <summary>
    /// When the user signed in and this session was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Last time this session was used to mint a token or SSO into another client.
    /// </summary>
    public DateTimeOffset LastActivityAt { get; set; }

    /// <summary>
    /// Absolute expiry, independent of activity.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// Best-effort, from the request that created this session.
    /// </summary>
    public string? IpAddress { get; set; }

    /// <summary>
    /// Best-effort, from the request that created this session.
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// When this session was explicitly revoked (sign-out or admin action); <see langword="null"/> while live.
    /// </summary>
    public DateTimeOffset? RevokedAt { get; set; }
}