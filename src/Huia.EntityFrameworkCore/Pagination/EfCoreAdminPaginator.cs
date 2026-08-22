using Huia.Endpoints.Admin;
using Huia.Identity;
using Huia.Pagination;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using MR.AspNetCore.Pagination;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;

namespace Huia.EntityFrameworkCore.Pagination;

/// <summary>
/// EF Core-backed <see cref="IAdminEfCorePaginator"/>, registered by
/// <c>WithEntityFrameworkStores&lt;TContext&gt;()</c> — queries OpenIddict's/Identity's EF Core entities
/// directly via <typeparamref name="TContext"/> and paginates them with MR.AspNetCore.Pagination's keyset
/// pagination, reusing each admin endpoint's own <c>ToResponseAsync</c> DTO mapping (which already works
/// against any <c>object</c> of the manager's recognized entity type, whether it came from
/// <c>manager.ListAsync(...)</c> or - as here - a raw EF Core query).
/// </summary>
internal sealed class EfCoreAdminPaginator<TContext>(
    TContext dbContext,
    IPaginationService paginationService,
    IOpenIddictApplicationManager applicationManager,
    IOpenIddictScopeManager scopeManager,
    IOpenIddictAuthorizationManager authorizationManager,
    HuiaUserManager userManager) : IAdminEfCorePaginator
    where TContext : DbContext
{
    public async Task<IResult> ListApplicationsAsync(int? pageSize, CancellationToken cancellationToken)
    {
        var applications = dbContext.Set<OpenIddictEntityFrameworkCoreApplication>();
        var page = await paginationService.KeysetPaginateAsync(
            applications,
            b => b.Ascending(a => a.Id!),
            id => applications.FirstOrDefaultAsync(a => a.Id == id, cancellationToken),
            q => q,
            pageSize).ConfigureAwait(false);

        var items = new List<ApplicationsEndpoints.ApplicationResponse>(page.Data.Count);
        foreach (var application in page.Data)
        {
            items.Add(await ApplicationsEndpoints.ToResponseAsync(application, applicationManager, cancellationToken)
                .ConfigureAwait(false));
        }

        return Results.Ok(new KeysetPaginationResult<ApplicationsEndpoints.ApplicationResponse>(
            items, page.TotalCount, page.PageSize, page.HasPrevious, page.HasNext));
    }

    public async Task<IResult> ListScopesAsync(int? pageSize, CancellationToken cancellationToken)
    {
        var scopes = dbContext.Set<OpenIddictEntityFrameworkCoreScope>();
        var page = await paginationService.KeysetPaginateAsync(
            scopes,
            b => b.Ascending(s => s.Id!),
            id => scopes.FirstOrDefaultAsync(s => s.Id == id, cancellationToken),
            q => q,
            pageSize).ConfigureAwait(false);

        var items = new List<ScopesEndpoints.ScopeResponse>(page.Data.Count);
        foreach (var scope in page.Data)
        {
            items.Add(await ScopesEndpoints.ToResponseAsync(scope, scopeManager, cancellationToken)
                .ConfigureAwait(false));
        }

        return Results.Ok(new KeysetPaginationResult<ScopesEndpoints.ScopeResponse>(
            items, page.TotalCount, page.PageSize, page.HasPrevious, page.HasNext));
    }

    public async Task<IResult> ListAuthorizationsAsync(int? pageSize, string? subject, string? applicationId,
        CancellationToken cancellationToken)
    {
        var authorizations = dbContext.Set<OpenIddictEntityFrameworkCoreAuthorization>().AsQueryable();
        if (!string.IsNullOrWhiteSpace(subject))
        {
            authorizations = authorizations.Where(a => a.Subject == subject);
        }

        if (applicationId is not null)
        {
            // ApplicationId is a shadow FK property (no CLR member on the entity) - filter via the
            // navigation instead, which EF Core translates to the same join.
            authorizations = authorizations.Where(a => a.Application!.Id == applicationId);
        }

        var page = await paginationService.KeysetPaginateAsync(
            authorizations,
            b => b.Ascending(a => a.Id!),
            id => authorizations.FirstOrDefaultAsync(a => a.Id == id, cancellationToken),
            q => q,
            pageSize).ConfigureAwait(false);

        var items = new List<AuthorizationsEndpoints.AuthorizationResponse>(page.Data.Count);
        foreach (var authorization in page.Data)
        {
            items.Add(await AuthorizationsEndpoints.ToResponseAsync(authorization, authorizationManager,
                cancellationToken).ConfigureAwait(false));
        }

        return Results.Ok(new KeysetPaginationResult<AuthorizationsEndpoints.AuthorizationResponse>(
            items, page.TotalCount, page.PageSize, page.HasPrevious, page.HasNext));
    }

    public async Task<IResult> ListUsersAsync(string? search, int? pageSize, CancellationToken cancellationToken)
    {
        var users = dbContext.Set<HuiaUser>().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            users = users.Where(u => u.Email!.Contains(search) || u.UserName!.Contains(search));
        }

        // Email isn't guaranteed unique, so it alone isn't a valid keyset column - Id breaks ties and keeps
        // the ordering fully deterministic, which keyset pagination requires.
        var page = await paginationService.KeysetPaginateAsync(
            users,
            b => b.Ascending(u => u.Email!).Ascending(u => u.Id),
            id => users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken),
            q => q,
            pageSize).ConfigureAwait(false);

        var items = new List<UsersEndpoints.UserResponse>(page.Data.Count);
        foreach (var user in page.Data)
        {
            items.Add(await UsersEndpoints.ToResponseAsync(user, userManager).ConfigureAwait(false));
        }

        return Results.Ok(new KeysetPaginationResult<UsersEndpoints.UserResponse>(
            items, page.TotalCount, page.PageSize, page.HasPrevious, page.HasNext));
    }
}
