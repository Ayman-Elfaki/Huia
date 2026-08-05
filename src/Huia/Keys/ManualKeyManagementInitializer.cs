using Microsoft.Extensions.Hosting;

namespace Huia.Keys;

/// <summary>
/// Populates <see cref="KeyManager.Snapshot"/> from whatever keys already exist in the store before the
/// app starts accepting requests, under <c>huia.KeysManagement.UseManualKeyManagement()</c>. Unlike
/// <see cref="KeyManagementInitializer"/> (used by <c>UseAutomaticKeyManagement()</c>), this never
/// creates or retires a key itself — the app is
/// responsible for calling <see cref="KeyManager.CreateKeyAsync"/>/<see cref="KeyManager.RetireKeyAsync"/> itself.
/// </summary>
internal sealed class ManualKeyManagementInitializer(KeyManager keyManager) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => keyManager.RefreshSnapshotAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}