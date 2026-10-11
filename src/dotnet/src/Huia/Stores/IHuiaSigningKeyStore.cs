using Huia.OpenId.Entities;

namespace Huia.Stores;

/// <summary>Filter for <see cref="IHuiaSigningKeyStore.QueryAsync"/>. Unset members match everything.</summary>
public sealed record HuiaSigningKeyFilter
{
    /// <summary>Only keys of this tenant.</summary>
    public string? TenantId { get; init; }

    /// <summary>Only keys in one of these lifecycle states.</summary>
    public IReadOnlyCollection<HuiaSigningKeyStatus>? Statuses { get; init; }
}

/// <summary>Keyset-paginated listing parameters for <see cref="IHuiaSigningKeyStore.ListAsync"/>.</summary>
public sealed record HuiaSigningKeyQuery
{
    /// <summary>Restrict the page to one tenant.</summary>
    public string? TenantId { get; init; }

    /// <summary>Page size.</summary>
    public int PageSize { get; init; } = 25;

    /// <summary>Return the rows after this key id.</summary>
    public string? After { get; init; }

    /// <summary>Return the rows before this key id.</summary>
    public string? Before { get; init; }
}

/// <summary>
/// Persistence port for the per-tenant signing keys. All reads are tenant-explicit (no ambient tenant is
/// needed, so jobs and the readiness check can use it); the lifecycle rules live in the callers
/// (<c>HuiaKeyLifecycleService</c>, the admin endpoints) and a store only has to load and save rows.
/// </summary>
public interface IHuiaSigningKeyStore
{
    /// <summary>Returns every key matching <paramref name="filter"/>, unordered, as detached rows.</summary>
    Task<IReadOnlyList<HuiaSigningKey>> QueryAsync(HuiaSigningKeyFilter filter, CancellationToken cancellationToken = default);

    /// <summary>Returns one key by id, or <c>null</c>, as a detached row.</summary>
    Task<HuiaSigningKey?> FindAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Returns a keyset-paginated page of keys ordered by id.</summary>
    Task<IHuiaPageResult<HuiaSigningKey>> ListAsync(HuiaSigningKeyQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the given changes atomically: inserts <paramref name="added"/>, writes the new column
    /// values of <paramref name="updated"/> (rows previously returned by this store and mutated) and
    /// deletes <paramref name="removed"/>.
    /// </summary>
    Task SaveAsync(
        IReadOnlyCollection<HuiaSigningKey> added,
        IReadOnlyCollection<HuiaSigningKey> updated,
        IReadOnlyCollection<HuiaSigningKey> removed,
        CancellationToken cancellationToken = default);
}
