using Huia.Common;
using OpenIddict.Abstractions;
using Quartz;

namespace Huia.Sessions;

/// <summary>
/// Quartz job that revokes-with-notification any live session idle past <see
/// cref="UserSessionsOptions.IdleTimeout"/> — Keycloak's "SSO Session Idle" concept. Unlike
/// <c>HuiaUserSessionPruningJob</c> (which hard-deletes already-dead rows and needs direct <c>DbContext</c>
/// access), this only needs the provider-agnostic <see cref="IUserSessionManager"/>/<see
/// cref="UserSessionService"/> abstractions, so it works against any persistence backend and lives in core
/// <c>Huia</c> rather than <c>Huia.EntityFrameworkCore</c> — the same split <c>KeyRotationJob</c> follows
/// for key management.
/// </summary>
[DisallowConcurrentExecution]
internal sealed class UserSessionTimeoutJob(
    IUserSessionManager manager,
    UserSessionService sessions,
    UserSessionsOptions options,
    LogoutNotifier logoutNotifier,
    IOpenIddictAuthorizationManager authorizationManager,
    TimeProvider timeProvider) : IJob
{
    private const int BatchSize = 100;

    public Task Execute(IJobExecutionContext jobContext) => RunAsync(jobContext.CancellationToken);

    /// <summary>
    /// The actual scan-and-revoke pass, split out from <see cref="Execute"/> so it can be invoked directly
    /// (via DI, bypassing Quartz's own scheduler/trigger machinery) — in particular by tests, which would
    /// otherwise depend on <c>IScheduler.TriggerJob</c> timing and Quartz's default, process-wide
    /// <c>SchedulerRepository</c> naming (shared across every host in the same test process unless each one
    /// configures a distinct scheduler name).
    /// </summary>
    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var offset = 0;

        while (true)
        {
            var batch = await manager.ListAsync(BatchSize, offset, userId: null, cancellationToken)
                .ConfigureAwait(false);
            if (batch.Count == 0)
            {
                break;
            }

            // Offsets stay stable across pages even as sessions in this same batch get revoked below:
            // RevokeWithNotificationAsync sets RevokedAt rather than deleting the row (deletion is
            // HuiaUserSessionPruningJob's separate, already-existing job), so the total row count — and
            // therefore what each offset points at — doesn't shift mid-scan.
            foreach (var session in batch.Where(s => s.IsLive(now) && now - s.LastActivityAt > options.IdleTimeout))
            {
                await sessions.RevokeWithNotificationAsync(session, logoutNotifier, authorizationManager,
                    cancellationToken).ConfigureAwait(false);
            }

            offset += BatchSize;
        }
    }
}
