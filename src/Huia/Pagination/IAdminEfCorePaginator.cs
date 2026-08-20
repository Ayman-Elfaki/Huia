using Microsoft.AspNetCore.Http;

namespace Huia.Pagination;

/// <summary>
/// Optional keyset-pagination backend for the admin list endpoints, resolved via DI. Registered only when
/// <c>.WithEntityFrameworkStores&lt;TContext&gt;()</c> is configured (see
/// <c>Huia.EntityFrameworkCore.Pagination.EfCoreAdminPaginator{TContext}</c>) — a consumer using
/// <c>.WithStore&lt;TStore,...&gt;()</c> instead never registers an implementation, so the admin endpoints
/// resolve <see langword="null"/> and fall back to their own store-agnostic offset-cursor pagination
/// (<see cref="Huia.Common.OffsetCursor"/>/<see cref="Huia.Common.PagedResult{T}"/>) unchanged.
/// </summary>
/// <remarks>
/// Deliberately returns <see cref="Task{IResult}"/> rather than any <c>MR.AspNetCore.Pagination</c> type, so
/// this interface — and <c>src/Huia</c> as a whole — never needs to reference that package or EF Core.
/// Implementations read pagination cursors (<c>after</c>/<c>before</c>/<c>first</c>/<c>last</c>) directly off
/// the ambient <see cref="Microsoft.AspNetCore.Http.HttpContext"/>'s query string themselves, per
/// MR.AspNetCore.Pagination's own binding model — not via a parameter here.
/// </remarks>
public interface IAdminEfCorePaginator
{
    Task<IResult> ListApplicationsAsync(int? pageSize, CancellationToken cancellationToken);

    Task<IResult> ListScopesAsync(int? pageSize, CancellationToken cancellationToken);

    Task<IResult> ListAuthorizationsAsync(int? pageSize, string? subject, string? applicationId,
        CancellationToken cancellationToken);

    Task<IResult> ListUsersAsync(string? search, int? pageSize, CancellationToken cancellationToken);
}
