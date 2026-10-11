using Huia.Options;
using Huia.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Huia.Multitenancy;

/// <summary>Lets callers (the admin API, start-up seeders) control when the persisted tenants are loaded.</summary>
public interface IHuiaTenantRefresher
{
    /// <summary>Reloads the persisted tenants and swaps them into the provider, notifying listeners of new ones.</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the persisted tenants once if that has not happened yet (a no-op afterwards). The first load is
    /// silent: start-up work that runs after it already sees every tenant, so listeners are not told about them.
    /// </summary>
    Task EnsureLoadedAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Told when tenants appear at runtime (created through the tenant store, possibly on another instance), so
/// per-tenant bootstrap work — signing key, roles, scopes, clients — can run for them. Resolved from a scope
/// created for each notification.
/// </summary>
public interface IHuiaTenantChangeListener
{
    /// <summary>Called with the ids of tenants that were not in the previous snapshot.</summary>
    Task OnTenantsAddedAsync(IReadOnlyCollection<string> tenantIds, CancellationToken cancellationToken);
}

/// <summary>Convenience for start-up code that must see persisted tenants before it iterates them.</summary>
public static class HuiaTenantRefresherExtensions
{
    /// <summary>Ensures persisted tenants are loaded when persisted tenants are enabled; otherwise does nothing.</summary>
    public static Task EnsureTenantsLoadedAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.GetService<IHuiaTenantRefresher>()?.EnsureLoadedAsync(cancellationToken) ?? Task.CompletedTask;
    }
}

/// <summary>
/// An <see cref="IHuiaTenantProvider"/> that serves the code-first tenants of <see cref="HuiaOptions"/> plus the
/// tenants persisted in <see cref="IHuiaTenantStore"/>. Reads are synchronous against an immutable snapshot that
/// is rebuilt from the store at start-up, on <see cref="RefreshAsync"/>, and on a timer so other instances
/// converge. A code-first tenant always wins over a stored one with the same id.
/// </summary>
internal sealed partial class HuiaStoredTenantProvider(
    HuiaOptions options,
    IServiceScopeFactory scopeFactory,
    HuiaTenantProviderHolder holder,
    ILogger<HuiaStoredTenantProvider> logger)
    : BackgroundService, IHuiaTenantProvider, IHuiaTenantRefresher
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(60);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile IReadOnlyDictionary<string, HuiaTenant> _snapshot = BuildSnapshot(options, []);
    private bool _loaded;

    /// <summary>Installs this provider behind the shared holder so registration-time wiring sees stored tenants.</summary>
    public void Install() => holder.Use(this);

    public HuiaTenant this[string key] => _snapshot[key];

    public IEnumerable<string> Keys => _snapshot.Keys;

    public IEnumerable<HuiaTenant> Values => _snapshot.Values;

    public int Count => _snapshot.Count;

    public bool ContainsKey(string key) => _snapshot.ContainsKey(key);

    public bool TryGetValue(string key, out HuiaTenant value) => _snapshot.TryGetValue(key, out value!);

    public IEnumerator<KeyValuePair<string, HuiaTenant>> GetEnumerator() => _snapshot.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (_loaded)
        {
            return;
        }

        await RefreshAsync(cancellationToken);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        IReadOnlyList<string> added;
        try
        {
            IReadOnlyList<HuiaStoredTenant> stored;
            await using (var scope = scopeFactory.CreateAsyncScope())
            {
                var store = scope.ServiceProvider.GetRequiredService<IHuiaTenantStore>();
                stored = await store.ListAsync(cancellationToken);
            }

            var previous = _snapshot;
            var next = BuildSnapshot(options, stored);
            _snapshot = next;

            // The first load is silent (see IHuiaTenantRefresher.EnsureLoadedAsync).
            added = _loaded ? [.. next.Keys.Where(id => !previous.ContainsKey(id))] : [];
            _loaded = true;
            LogRefreshed(stored.Count);
        }
        finally
        {
            _gate.Release();
        }

        if (added.Count > 0)
        {
            await NotifyAsync(added, cancellationToken);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(RefreshInterval);
        do
        {
            try
            {
                if (_loaded)
                {
                    await RefreshAsync(stoppingToken);
                }
                else
                {
                    await EnsureLoadedAsync(stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Keep serving the last good snapshot; the next tick retries.
                LogRefreshFailed(ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task NotifyAsync(IReadOnlyList<string> added, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        foreach (var listener in scope.ServiceProvider.GetServices<IHuiaTenantChangeListener>())
        {
            try
            {
                await listener.OnTenantsAddedAsync(added, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogListenerFailed(ex, string.Join(", ", added));
            }
        }
    }

    private static IReadOnlyDictionary<string, HuiaTenant> BuildSnapshot(
        HuiaOptions options, IReadOnlyList<HuiaStoredTenant> stored)
    {
        var merged = new Dictionary<string, HuiaTenant>(StringComparer.Ordinal);
        foreach (var tenant in stored)
        {
            merged[tenant.TenantId] = tenant.Tenant;
        }

        foreach (var (id, tenant) in options.Tenants)
        {
            merged[id] = tenant;
        }

        return merged;
    }

    [LoggerMessage(LogLevel.Debug, "Refreshed tenant snapshot: {Count} persisted tenant(s).")]
    partial void LogRefreshed(int count);

    [LoggerMessage(LogLevel.Warning, "Refreshing persisted tenants failed; serving the previous snapshot.")]
    partial void LogRefreshFailed(Exception exception);

    [LoggerMessage(LogLevel.Error, "A tenant change listener failed for new tenant(s) {TenantIds}.")]
    partial void LogListenerFailed(Exception exception, string tenantIds);
}
