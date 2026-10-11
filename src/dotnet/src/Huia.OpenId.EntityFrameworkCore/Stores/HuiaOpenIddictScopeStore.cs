using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using OpenIddict.EntityFrameworkCore;
using OpenIddict.EntityFrameworkCore.Models;

namespace Huia.OpenId.EntityFrameworkCore.Stores;

/// <summary>
/// OpenIddict scope store that scopes lookups to the tenant currently in scope using the <c>TenantId</c>
/// column on the scope row (a shadow property configured by <see cref="HuiaDbContext{TUser, TRole, TId}"/>),
/// so one tenant's scopes are invisible to another and the same scope name may exist in several tenants.
/// With no tenant in scope (start-up seeding, admin listing) lookups are unfiltered.
/// </summary>
public sealed class HuiaOpenIddictScopeStore : OpenIddictEntityFrameworkCoreScopeStore
{
    private readonly IMultiTenantContextAccessor _tenantAccessor;
    private readonly IOpenIddictEntityFrameworkCoreContext _context;

    /// <summary>Creates the store.</summary>
    public HuiaOpenIddictScopeStore(
        IMemoryCache cache,
        IOpenIddictEntityFrameworkCoreContext context,
        IOptionsMonitor<OpenIddictEntityFrameworkCoreOptions> options,
        IMultiTenantContextAccessor tenantAccessor)
        : base(cache, context, options)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _tenantAccessor = tenantAccessor ?? throw new ArgumentNullException(nameof(tenantAccessor));
    }

    /// <inheritdoc />
    public override async ValueTask CreateAsync(
        OpenIddictEntityFrameworkCoreScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var db = await _context.GetDbContextAsync(cancellationToken);
        db.Entry(scope).Property<string>(HuiaOpenIddictApplicationStore.TenantIdProperty).CurrentValue =
            TenantStamp.Resolve(scope.Properties, _tenantAccessor.CurrentTenantId());

        await base.CreateAsync(scope, cancellationToken);
    }

    /// <inheritdoc />
    public override async ValueTask<OpenIddictEntityFrameworkCoreScope?> FindByIdAsync(
        string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);
        var query = await ScopedAsync(cancellationToken);
        return await query.FirstOrDefaultAsync(s => s.Id == identifier, cancellationToken);
    }

    /// <inheritdoc />
    public override async ValueTask<OpenIddictEntityFrameworkCoreScope?> FindByNameAsync(
        string name, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var query = await ScopedAsync(cancellationToken);
        return await query.FirstOrDefaultAsync(s => s.Name == name, cancellationToken);
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<OpenIddictEntityFrameworkCoreScope> FindByNamesAsync(
        ImmutableArray<string> names, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (names.IsDefaultOrEmpty)
        {
            yield break;
        }

        var wanted = names.ToList();
        var query = await ScopedAsync(cancellationToken);
        await foreach (var scope in query.Where(s => wanted.Contains(s.Name!))
                           .AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            yield return scope;
        }
    }

    private async Task<IQueryable<OpenIddictEntityFrameworkCoreScope>> ScopedAsync(CancellationToken cancellationToken)
    {
        var db = await _context.GetDbContextAsync(cancellationToken);
        IQueryable<OpenIddictEntityFrameworkCoreScope> query = db.Set<OpenIddictEntityFrameworkCoreScope>().AsTracking();

        var tenant = _tenantAccessor.CurrentTenantId();
        return tenant is null
            ? query
            : query.Where(s => EF.Property<string>(s, HuiaOpenIddictApplicationStore.TenantIdProperty) == tenant);
    }
}
