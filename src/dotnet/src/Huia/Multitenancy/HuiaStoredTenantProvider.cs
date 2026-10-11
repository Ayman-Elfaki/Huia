using Huia.Options;
using Huia.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Huia.Multitenancy;

/// <summary>Lets callers (the admin API) make the tenant provider pick up a just-saved tenant immediately.</summary>
public interface IHuiaTenantRefresher
{
    /// <summary>Reloads the persisted tenants and swaps them into the provider.</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);
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
    ILogger<HuiaStoredTenantProvider> logger)
    : BackgroundService, IHuiaTenantProvider, IHuiaTenantRefresher
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(60);

    private volatile IReadOnlyDictionary<string, HuiaTenant> _snapshot = BuildSnapshot(options, []);

    public HuiaTenant this[string key] => _snapshot[key];

    public IEnumerable<string> Keys => _snapshot.Keys;

    public IEnumerable<HuiaTenant> Values => _snapshot.Values;

    public int Count => _snapshot.Count;

    public bool ContainsKey(string key) => _snapshot.ContainsKey(key);

    public bool TryGetValue(string key, out HuiaTenant value) => _snapshot.TryGetValue(key, out value!);

    public IEnumerator<KeyValuePair<string, HuiaTenant>> GetEnumerator() => _snapshot.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IHuiaTenantStore>();
        var stored = await store.ListAsync(cancellationToken);

        _snapshot = BuildSnapshot(options, stored);
        LogRefreshed(stored.Count);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(RefreshInterval);
        do
        {
            try
            {
                await RefreshAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Keep serving the last good snapshot; the next tick retries.
                LogRefreshFailed(ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
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
}
