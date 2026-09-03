using Huia.EntityFrameworkCore;

namespace Huia.IdentityServer;

/// <summary>
/// Creates the database schema at start-up (the library ships no migrations). Retries the initial
/// connection so it tolerates the database container still coming up when there is no orchestrator
/// readiness gate.
/// </summary>
internal sealed class SchemaInitializer(IServiceProvider services, ILogger<SchemaInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var scope = services.CreateAsyncScope();
                var context = scope.ServiceProvider.GetRequiredService<HuiaDbContext>();
                await context.Database.EnsureCreatedAsync(cancellationToken);
                return;
            }
            catch (Exception ex) when (attempt < 20 && ex is not OperationCanceledException)
            {
                logger.LogWarning("Schema init attempt {Attempt} failed ({Message}); retrying…", attempt, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(attempt, 5)), cancellationToken);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
