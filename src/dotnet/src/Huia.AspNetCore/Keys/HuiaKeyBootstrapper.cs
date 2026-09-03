using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Huia.AspNetCore.Keys;

/// <summary>
/// Ensures every configured tenant has an active signing key before the first request. Registered right
/// after the database context so it runs before OpenIddict serves anything (hosted-service order is
/// registration order).
/// </summary>
internal sealed partial class HuiaKeyBootstrapper(IServiceProvider services, ILogger<HuiaKeyBootstrapper> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = services.CreateAsyncScope();
            var lifecycle = scope.ServiceProvider.GetRequiredService<HuiaKeyLifecycleService>();
            await lifecycle.EnsureBootstrappedAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(ex);
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(LogLevel.Error, "Signing-key bootstrap failed.")]
    partial void LogFailed(Exception exception);
}
