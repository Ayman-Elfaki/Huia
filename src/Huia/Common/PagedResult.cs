namespace Huia.Common;

/// <summary>
/// A page of results returned by the admin API's list endpoints. <paramref name="NextCursor"/> is an
/// opaque token to pass back as the next request's <c>cursor</c> query parameter to fetch the following
/// page, or <see langword="null"/> if <paramref name="Items"/> is the last page.
/// </summary>
internal sealed record PagedResult<T>(IReadOnlyList<T> Items, string? NextCursor);