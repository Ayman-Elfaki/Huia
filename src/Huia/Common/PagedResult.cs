namespace Huia.Common;

/// <summary>
/// A page of results returned by an admin list endpoint's store-agnostic offset-cursor fallback (used when no
/// <c>IAdminEfCorePaginator</c> is registered — see <c>OffsetCursor</c>). Mirrors
/// <c>MR.AspNetCore.Pagination</c>'s <c>KeysetPaginationResult&lt;T&gt;</c> field-for-field (the shape the EF
/// Core-backed path returns — see <c>Huia.EntityFrameworkCore.Pagination.EfCoreAdminPaginator</c>) so every
/// admin list endpoint returns the same properties regardless of which store backs it, the same way
/// <c>Endpoints.Admin.RolesEndpoints</c>'s own <c>RolesPage&lt;T&gt;</c> already does for its (unpaginated)
/// list. <paramref name="NextCursor"/> is additional to that shape: an opaque token to pass back as the next
/// request's <c>cursor</c> query parameter to fetch the following page, or <see langword="null"/> if
/// <paramref name="Data"/> is the last page.
/// </summary>
internal sealed record PagedResult<T>(
    IReadOnlyList<T> Data,
    int TotalCount,
    int PageSize,
    bool HasPrevious,
    bool HasNext,
    string? NextCursor);
