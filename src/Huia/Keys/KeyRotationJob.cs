using Quartz;

namespace Huia.Keys;

/// <summary>Quartz job that performs the periodic key rotation check and purges expired keys.</summary>
[DisallowConcurrentExecution]
public sealed class KeyRotationJob(KeyManager keyManager) : IJob
{
    /// <summary>Runs one rotation check and purge pass, per Quartz's <see cref="IJob"/> contract.</summary>
    public async Task Execute(IJobExecutionContext context)
    {
        await keyManager.RotateAsync(context.CancellationToken).ConfigureAwait(false);
        await keyManager.PurgeExpiredKeysAsync(context.CancellationToken).ConfigureAwait(false);
    }
}