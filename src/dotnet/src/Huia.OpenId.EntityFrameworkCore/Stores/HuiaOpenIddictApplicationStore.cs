using System.Text.Json;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using OpenIddict.EntityFrameworkCore;
using OpenIddict.EntityFrameworkCore.Models;

namespace Huia.OpenId.EntityFrameworkCore.Stores;

/// <summary>
/// OpenIddict application store that scopes lookups to the tenant currently in scope using the
/// <c>TenantId</c> column on the application row (a shadow property configured by
/// <see cref="HuiaDbContext{TUser, TRole, TId}"/>), so one tenant's clients are invisible to another and the
/// same <c>client_id</c> may exist in several tenants. With no tenant in scope (start-up seeding, admin
/// listing) lookups are unfiltered.
/// </summary>
public sealed class HuiaOpenIddictApplicationStore : OpenIddictEntityFrameworkCoreApplicationStore
{
    /// <summary>The name of the shadow property holding the owning tenant.</summary>
    internal const string TenantIdProperty = "TenantId";

    private readonly IMultiTenantContextAccessor _tenantAccessor;
    private readonly IOpenIddictEntityFrameworkCoreContext _context;

    /// <summary>Creates the store.</summary>
    public HuiaOpenIddictApplicationStore(
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
        OpenIddictEntityFrameworkCoreApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        var db = await _context.GetDbContextAsync(cancellationToken);
        db.Entry(application).Property<string>(TenantIdProperty).CurrentValue =
            TenantStamp.Resolve(application.Properties, _tenantAccessor.CurrentTenantId());

        await base.CreateAsync(application, cancellationToken);
    }

    /// <inheritdoc />
    public override async ValueTask<OpenIddictEntityFrameworkCoreApplication?> FindByClientIdAsync(
        string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);
        var query = await ScopedAsync(cancellationToken);
        return await query.FirstOrDefaultAsync(a => a.ClientId == identifier, cancellationToken);
    }

    /// <inheritdoc />
    public override async ValueTask<OpenIddictEntityFrameworkCoreApplication?> FindByIdAsync(
        string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);
        var query = await ScopedAsync(cancellationToken);
        return await query.FirstOrDefaultAsync(a => a.Id == identifier, cancellationToken);
    }

    private async Task<IQueryable<OpenIddictEntityFrameworkCoreApplication>> ScopedAsync(CancellationToken cancellationToken)
    {
        var db = await _context.GetDbContextAsync(cancellationToken);
        IQueryable<OpenIddictEntityFrameworkCoreApplication> query =
            db.Set<OpenIddictEntityFrameworkCoreApplication>().AsTracking();

        var tenant = _tenantAccessor.CurrentTenantId();
        return tenant is null
            ? query
            : query.Where(a => EF.Property<string>(a, TenantIdProperty) == tenant);
    }
}

/// <summary>Resolves the tenant to stamp on a new OpenIddict row.</summary>
internal static class TenantStamp
{
    /// <summary>
    /// Returns the tenant recorded in the row's <c>Properties</c> JSON (<c>huia:tenant</c>, written by Huia's
    /// descriptor mapper and the admin API), falling back to the tenant in scope, then to the empty string
    /// (a row no tenant can see).
    /// </summary>
    public static string Resolve(string? propertiesJson, string? currentTenant)
    {
        if (!string.IsNullOrWhiteSpace(propertiesJson))
        {
            try
            {
                using var document = JsonDocument.Parse(propertiesJson);
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty(HuiaConstants.ApplicationProperties.Tenant, out var value)
                    && value.ValueKind == JsonValueKind.String
                    && value.GetString() is { Length: > 0 } tenant)
                {
                    return tenant;
                }
            }
            catch (JsonException)
            {
                // Malformed properties never carry a tenant; fall through.
            }
        }

        return currentTenant ?? string.Empty;
    }
}
