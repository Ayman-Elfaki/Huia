using Huia.Stores;
using Microsoft.EntityFrameworkCore;
using MR.AspNetCore.Pagination;
using OpenIddict.EntityFrameworkCore.Models;

namespace Huia.OpenId.EntityFrameworkCore;

/// <summary>
/// EF Core implementation of <see cref="IHuiaOpenIddictAdminStore"/>. Reads the OpenIddict tables directly
/// (not through the tenant-scoped OpenIddict stores), so it sees every tenant's rows.
/// </summary>
internal sealed class HuiaOpenIdEfCoreOpenIddictAdminStore<TContext>(TContext db, IPaginationService pagination)
    : IHuiaOpenIddictAdminStore
    where TContext : DbContext
{
    public async Task<IHuiaPageResult<HuiaClientRecord>> ListClientsAsync(
        HuiaClientQuery query, CancellationToken cancellationToken)
    {
        var result = await pagination.KeysetPaginateAsync(
            db.Set<OpenIddictEntityFrameworkCoreApplication>().AsNoTracking(),
            builder => builder.Ascending(a => a.Id!),
            async id => await db.Set<OpenIddictEntityFrameworkCoreApplication>().FindAsync([id], cancellationToken),
            apps => apps.Select(a => new HuiaClientRecord(a.Id!, a.ClientId, a.DisplayName, a.ClientType, a.Properties)),
            new KeysetQueryModel
            {
                Size = query.PageSize,
                After = query.After,
                Before = query.Before,
                First = query.After == null && query.Before == null,
                Last = false,
            });

        return new HuiaEfPageResult<HuiaClientRecord>(result, c => c.Id);
    }

    public Task<HuiaClientRecord?> FindClientAsync(string id, CancellationToken cancellationToken)
        => db.Set<OpenIddictEntityFrameworkCoreApplication>().AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new HuiaClientRecord(a.Id!, a.ClientId, a.DisplayName, a.ClientType, a.Properties))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<HuiaScopeRecord>> ListScopesAsync(CancellationToken cancellationToken)
        => await db.Set<OpenIddictEntityFrameworkCoreScope>().AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => new HuiaScopeRecord(s.Id!, s.Name, s.DisplayName, s.Description, s.Properties))
            .ToListAsync(cancellationToken);
}

/// <summary>
/// EF Core implementation of <see cref="IHuiaPhoneLookupStore"/>. The tenant's global query filter applies,
/// so the lookup is scoped to the tenant currently in scope.
/// </summary>
internal sealed class HuiaOpenIdEfCorePhoneLookupStore<TContext, TUser>(TContext db) : IHuiaPhoneLookupStore
    where TContext : DbContext
    where TUser : Huia.OpenId.Entities.HuiaUser
{
    public async Task<Huia.OpenId.Entities.HuiaUser?> FindByPhoneNumberAsync(
        string phoneNumber, CancellationToken cancellationToken)
        => await db.Set<TUser>().FirstOrDefaultAsync(u => u.PhoneNumber == phoneNumber, cancellationToken);
}

/// <summary>EF Core implementation of <see cref="IHuiaDatabaseProbe"/>.</summary>
internal sealed class HuiaOpenIdEfCoreDatabaseProbe<TContext>(TContext db) : IHuiaDatabaseProbe
    where TContext : DbContext
{
    public Task<bool> CanConnectAsync(CancellationToken cancellationToken)
        => db.Database.CanConnectAsync(cancellationToken);
}
