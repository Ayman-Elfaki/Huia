using Huia.DependencyInjection;
using Huia.OpenId.EntityFrameworkCore.Entities;
using Huia.OpenId.EntityFrameworkCore.Stores;
using Huia.Stores;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MR.AspNetCore.Pagination;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore;
using OpenIddict.EntityFrameworkCore.Models;

namespace Huia.OpenId.EntityFrameworkCore;

/// <summary>
/// Extension methods that wire up EF Core persistence for <c>Huia.OpenId</c>.
/// Call this immediately after <c>AddHuiaOpenId(…)</c>:
/// <code>
/// services.AddHuiaOpenId(…).AddEntityFrameworkCoreStores&lt;HuiaDbContext, HuiaUser, HuiaRole&gt;();
/// </code>
/// </summary>
public static class HuiaOpenIdEntityFrameworkCoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers the EF Core <see cref="HuiaDbContext{TUser,TRole,TId}"/> as the OpenIddict persistence layer,
    /// wires up the tenant-scoped OpenIddict application/scope stores, registers the
    /// <see cref="IHuiaOpenIdAdminStore{TUser,TRole}"/> implementation, and replaces Identity's
    /// UserStore/RoleStore with the EF Core variants.
    /// </summary>
    /// <typeparam name="TContext">The host's concrete <c>DbContext</c>.</typeparam>
    /// <typeparam name="TUser">The concrete user entity, at least <see cref="HuiaUser"/>.</typeparam>
    /// <typeparam name="TRole">The concrete role entity, at least <see cref="HuiaRole"/>.</typeparam>
    public static IHuiaBuilder AddEntityFrameworkCoreStores<TContext, TUser, TRole>(this IHuiaBuilder builder)
        where TContext : DbContext
        where TUser : HuiaUser, new()
        where TRole : HuiaRole, new()
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;

        if (services.All(d => d.ServiceType != typeof(DbContextOptions<TContext>)))
        {
            services.AddDbContext<TContext>();
        }

        ForwardHuiaBaseContexts(services, typeof(TContext));

        services
            .AddIdentityCore<TUser>(o => o.Stores.SchemaVersion = IdentitySchemaVersions.Version3)
            .AddRoles<TRole>()
            .AddEntityFrameworkStores<TContext>()
            .AddDefaultTokenProviders();

        services.Configure<IdentityOptions>(o => o.Stores.ProtectPersonalData = false);

        // Pagination service used by the admin list store
        services.AddPagination();

        // Wire OpenIddict's EF Core persistence to the registered DbContext
        services.AddOpenIddict()
            .AddCore(core => core.UseEntityFrameworkCore().UseDbContext<TContext>());

        // Tenant-scoped OpenIddict stores: clients and scopes are invisible across tenant boundaries
        services.RemoveAll(typeof(IOpenIddictApplicationStore<OpenIddictEntityFrameworkCoreApplication>));
        services.AddScoped<IOpenIddictApplicationStore<OpenIddictEntityFrameworkCoreApplication>, HuiaOpenIddictApplicationStore>();

        services.RemoveAll(typeof(IOpenIddictScopeStore<OpenIddictEntityFrameworkCoreScope>));
        services.AddScoped<IOpenIddictScopeStore<OpenIddictEntityFrameworkCoreScope>, HuiaOpenIddictScopeStore>();

        // EF Core admin store (powers Huia.OpenId's admin endpoints)
        services.AddScoped<IHuiaOpenIdAdminStore<TUser, TRole>>(sp =>
            new HuiaOpenIdEfCoreAdminStore<TContext, TUser, TRole>(
                sp.GetRequiredService<TContext>(),
                sp.GetRequiredService<IPaginationService>()));

        return builder;
    }

    /// <summary>Convenience overload for a host using the default <see cref="HuiaUser"/> / <see cref="HuiaRole"/> entities.</summary>
    public static IHuiaBuilder AddEntityFrameworkCoreStores<TContext>(this IHuiaBuilder builder)
        where TContext : DbContext
        => builder.AddEntityFrameworkCoreStores<TContext, HuiaUser, HuiaRole>();

    private static void ForwardHuiaBaseContexts(IServiceCollection services, Type contextType)
    {
        for (var baseType = contextType.BaseType; baseType is not null && baseType != typeof(DbContext); baseType = baseType.BaseType)
        {
            if (baseType.Namespace?.StartsWith("Huia", StringComparison.Ordinal) != true)
            {
                continue;
            }

            services.TryAddScoped(baseType, sp => sp.GetRequiredService(contextType));
        }
    }
}
