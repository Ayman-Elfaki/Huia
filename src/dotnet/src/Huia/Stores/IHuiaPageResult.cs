namespace Huia.Stores;

/// <summary>
/// The result returned by <see cref="IHuiaStore{TUser,TRole}"/> list operations.
/// Keyset-based stores populate the cursor fields; offset-based stores populate
/// <see cref="TotalCount"/> and <see cref="Page"/> instead.
/// </summary>
public interface IHuiaPageResult<out T>
{
    /// <summary>The items in this page.</summary>
    IReadOnlyList<T> Data { get; }

    /// <summary><c>true</c> when a next page exists.</summary>
    bool HasNext { get; }

    /// <summary><c>true</c> when a previous page exists.</summary>
    bool HasPrevious { get; }

    /// <summary>Opaque cursor pointing to the item after the last one on this page (keyset stores only).</summary>
    string? NextCursor { get; }

    /// <summary>Opaque cursor pointing to the item before the first one on this page (keyset stores only).</summary>
    string? PreviousCursor { get; }

    /// <summary>Total matching rows; offset stores may populate this, keyset stores leave it <c>null</c>.</summary>
    int? TotalCount { get; }

    /// <summary>Current 1-based page number (offset stores only).</summary>
    int? Page { get; }
}
