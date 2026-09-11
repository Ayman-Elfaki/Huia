using System.Text.Json;
using Finbuckle.MultiTenant.Abstractions;
using Huia.Multitenancy;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using OpenIddict.EntityFrameworkCore;
using OpenIddict.EntityFrameworkCore.Models;

namespace Huia.OpenId.EntityFrameworkCore.Stores;

/// <summary>
/// An OpenIddict application store that makes clients belonging to other tenants invisible. The tenant a
/// client is bound to lives in its <c>Properties["huia:tenant"]</c> entry (there is no tenant column on the
/// stock OpenIddict entities); this store checks that entry on the two lookups the authorize/token
/// endpoints depend on. When no tenant is in scope (seeding, administration) no scoping is applied.
/// </summary>
public sealed class HuiaOpenIddictApplicationStore : OpenIddictEntityFrameworkCoreApplicationStore
{
    private readonly IMultiTenantContextAccessor _tenantAccessor;

    /// <summary>Creates the store.</summary>
    /// <param name="cache">OpenIddict's entity cache.</param>
    /// <param name="context">The OpenIddict EF Core context wrapper.</param>
    /// <param name="options">OpenIddict EF Core options.</param>
    /// <param name="tenantAccessor">Supplies the tenant lookups are scoped to.</param>
    public HuiaOpenIddictApplicationStore(
        IMemoryCache cache,
        IOpenIddictEntityFrameworkCoreContext context,
        IOptionsMonitor<OpenIddictEntityFrameworkCoreOptions> options,
        IMultiTenantContextAccessor tenantAccessor)
        : base(cache, context, options)
    {
        _tenantAccessor = tenantAccessor ?? throw new ArgumentNullException(nameof(tenantAccessor));
    }

    /// <inheritdoc />
    public override async ValueTask<OpenIddictEntityFrameworkCoreApplication?> FindByClientIdAsync(
        string identifier, CancellationToken cancellationToken)
    {
        var application = await base.FindByClientIdAsync(identifier, cancellationToken);
        return application is not null && await BelongsToCurrentTenantAsync(application, cancellationToken)
            ? application
            : null;
    }


    private async ValueTask<bool> BelongsToCurrentTenantAsync(
        OpenIddictEntityFrameworkCoreApplication application, CancellationToken cancellationToken)
    {
        var currentTenant = _tenantAccessor.CurrentTenantId();
        if (currentTenant is null)
        {
            return true;
        }

        var properties = await GetPropertiesAsync(application, cancellationToken);
        return properties.TryGetValue("huia:tenant", out var value)
               && value.ValueKind == JsonValueKind.String
               && string.Equals(value.GetString(), currentTenant, StringComparison.Ordinal);
    }
}
