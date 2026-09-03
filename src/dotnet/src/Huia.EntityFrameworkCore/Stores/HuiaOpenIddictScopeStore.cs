using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Finbuckle.MultiTenant.Abstractions;
using Huia.EntityFrameworkCore.Multitenancy;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using OpenIddict.EntityFrameworkCore;
using OpenIddict.EntityFrameworkCore.Models;

namespace Huia.EntityFrameworkCore.Stores;

/// <summary>
/// An OpenIddict scope store that makes scopes belonging to other tenants invisible. The owning tenant
/// lives in the scope's <c>Properties["huia:tenant"]</c> entry (there is no tenant column on the stock
/// OpenIddict entities); this store checks that entry on the name lookups the authorize endpoint depends
/// on. When no tenant is in scope (seeding, administration) no scoping is applied.
/// </summary>
public sealed class HuiaOpenIddictScopeStore : OpenIddictEntityFrameworkCoreScopeStore
{
    private readonly IMultiTenantContextAccessor _tenantAccessor;

    /// <summary>Creates the store.</summary>
    /// <param name="cache">OpenIddict's entity cache.</param>
    /// <param name="context">The OpenIddict EF Core context wrapper.</param>
    /// <param name="options">OpenIddict EF Core options.</param>
    /// <param name="tenantAccessor">Supplies the tenant lookups are scoped to.</param>
    public HuiaOpenIddictScopeStore(
        IMemoryCache cache,
        IOpenIddictEntityFrameworkCoreContext context,
        IOptionsMonitor<OpenIddictEntityFrameworkCoreOptions> options,
        IMultiTenantContextAccessor tenantAccessor)
        : base(cache, context, options)
    {
        _tenantAccessor = tenantAccessor ?? throw new ArgumentNullException(nameof(tenantAccessor));
    }

    /// <inheritdoc />
    public override async ValueTask<OpenIddictEntityFrameworkCoreScope?> FindByIdAsync(
        string identifier, CancellationToken cancellationToken)
    {
        var scope = await base.FindByIdAsync(identifier, cancellationToken);
        return scope is not null && await BelongsToCurrentTenantAsync(scope, cancellationToken) ? scope : null;
    }

    /// <inheritdoc />
    public override async ValueTask<OpenIddictEntityFrameworkCoreScope?> FindByNameAsync(
        string name, CancellationToken cancellationToken)
    {
        var scope = await base.FindByNameAsync(name, cancellationToken);
        return scope is not null && await BelongsToCurrentTenantAsync(scope, cancellationToken) ? scope : null;
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<OpenIddictEntityFrameworkCoreScope> FindByNamesAsync(
        ImmutableArray<string> names, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var scope in base.FindByNamesAsync(names, cancellationToken).WithCancellation(cancellationToken))
        {
            if (await BelongsToCurrentTenantAsync(scope, cancellationToken))
            {
                yield return scope;
            }
        }
    }

    private async ValueTask<bool> BelongsToCurrentTenantAsync(
        OpenIddictEntityFrameworkCoreScope scope, CancellationToken cancellationToken)
    {
        var currentTenant = _tenantAccessor.CurrentTenantId();
        if (currentTenant is null)
        {
            return true;
        }

        var properties = await GetPropertiesAsync(scope, cancellationToken);
        return properties.TryGetValue(HuiaConstants.ApplicationProperties.Tenant, out var value)
               && value.ValueKind == JsonValueKind.String
               && string.Equals(value.GetString(), currentTenant, StringComparison.Ordinal);
    }
}
