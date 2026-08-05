namespace Huia.Common;

/// <summary>
/// A page of results returned by the admin API's list endpoints.
/// </summary>
internal sealed record PagedResult<T>(IReadOnlyList<T> Items, long TotalCount);