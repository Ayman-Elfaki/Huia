using Microsoft.Extensions.Hosting;

namespace Huia.Keys;

/// <summary>
/// Ensures active signing/encryption keys exist and <see cref="KeyManager.Snapshot"/> is populated
/// before the app starts accepting requests. Ordinary rotation afterwards is driven by <see cref="KeyRotationJob"/>.
/// </summary>
internal sealed class KeyManagementInitializer(KeyManager keyManager) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => keyManager.RotateAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}