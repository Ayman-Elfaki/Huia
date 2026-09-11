using Quartz;

namespace Huia.Keys.Jobs;

/// <summary>Creates pending successor keys for tenants whose active key has aged past the rotation interval.</summary>
[DisallowConcurrentExecution]
internal sealed class HuiaKeyRotationJob(HuiaKeyLifecycleService lifecycle) : IJob
{
    public Task Execute(IJobExecutionContext context) => lifecycle.RotateAsync(context.CancellationToken);
}

/// <summary>Promotes pending keys that have reached their activation time; demotes the previous active key.</summary>
[DisallowConcurrentExecution]
internal sealed class HuiaKeyActivationJob(HuiaKeyLifecycleService lifecycle) : IJob
{
    public Task Execute(IJobExecutionContext context) => lifecycle.ActivateDueAsync(context.CancellationToken);
}

/// <summary>Unpublishes rotated keys once their retention window has elapsed.</summary>
[DisallowConcurrentExecution]
internal sealed class HuiaKeyRetirementJob(HuiaKeyLifecycleService lifecycle) : IJob
{
    public Task Execute(IJobExecutionContext context) => lifecycle.RetireAsync(context.CancellationToken);
}

/// <summary>Permanently deletes retired keys after the grace period.</summary>
[DisallowConcurrentExecution]
internal sealed class HuiaKeyDeletionJob(HuiaKeyLifecycleService lifecycle) : IJob
{
    public Task Execute(IJobExecutionContext context) => lifecycle.DeleteAsync(context.CancellationToken);
}
