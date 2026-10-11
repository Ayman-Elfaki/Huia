using Huia.Options;

namespace Huia.Multitenancy;

/// <summary>
/// The set of tenants Huia currently serves, keyed by tenant id (also the base-path segment). Everything that
/// needs to ask "which tenants exist?" or "what is this tenant configured with?" reads it from here rather
/// than from <see cref="HuiaOptions.Tenants"/>, so the source of the tenant list can change (code-first
/// today; a persisted, runtime-editable store later) without touching every consumer.
/// </summary>
/// <remarks>
/// It is a read-only dictionary so existing lookups (<c>TryGetValue</c>, <c>ContainsKey</c>, <c>Keys</c>,
/// <c>Values</c>, enumeration) keep their shape. Implementations must be safe to read concurrently.
/// </remarks>
public interface IHuiaTenantProvider : IReadOnlyDictionary<string, HuiaTenant>
{
}

/// <summary>
/// The default <see cref="IHuiaTenantProvider"/>: the code-first tenants of the validated
/// <see cref="HuiaOptions"/> tree.
/// </summary>
/// <param name="options">The options tree the tenants are read from.</param>
internal sealed class HuiaOptionsTenantProvider(HuiaOptions options) : IHuiaTenantProvider
{
    private IDictionary<string, HuiaTenant> Source => options.Tenants;

    public HuiaTenant this[string key] => Source[key];

    public IEnumerable<string> Keys => Source.Keys;

    public IEnumerable<HuiaTenant> Values => Source.Values;

    public int Count => Source.Count;

    public bool ContainsKey(string key) => Source.ContainsKey(key);

    public bool TryGetValue(string key, out HuiaTenant value) => Source.TryGetValue(key, out value!);

    public IEnumerator<KeyValuePair<string, HuiaTenant>> GetEnumerator() => Source.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
