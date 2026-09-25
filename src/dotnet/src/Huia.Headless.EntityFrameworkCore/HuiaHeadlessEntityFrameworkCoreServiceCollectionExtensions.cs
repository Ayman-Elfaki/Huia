using Huia.DependencyInjection;
using Huia.Entities;
using Huia.Stores;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MR.AspNetCore.Pagination;

namespace Huia.Headless.EntityFrameworkCore;

/// <summary>
/// Extension methods that wire up EF Core persistence for <c>Huia.Headless</c>.
/// Call this immediately after <c>AddHuiaHeadless(…)</c>:
/// <code>
/// services.AddHuiaHeadless(…).AddEntityFrameworkCoreStores&lt;HuiaDbContext&lt;HuiaUser, HuiaRole, string&gt;, HuiaUser, HuiaRole&gt;();
/// </code>
/// </summary>
public static class HuiaHeadlessEntityFrameworkCoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers the EF Core Identity stores and the <see cref="IHuiaStore{TUser,TRole}"/>
    /// implementation for use by <c>Huia.Headless</c>'s admin endpoints.
    /// </summary>
    /// <typeparam name="TContext">The host's concrete <c>DbContext</c>.</typeparam>
    /// <typeparam name="TUser">Concrete user type, at least <see cref="HuiaUser"/>.</typeparam>
    /// <typeparam name="TRole">Concrete role type, at least <see cref="HuiaRole"/>.</typeparam>
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

        // Pagination service used by the headless list store
        services.AddPagination();

        // IHuiaStore<TUser, TRole> for admin list endpoints
        services.AddScoped<IHuiaStore<TUser, TRole>>(sp =>
            new HuiaHeadlessEfCoreStore<TContext, TUser, TRole>(
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
