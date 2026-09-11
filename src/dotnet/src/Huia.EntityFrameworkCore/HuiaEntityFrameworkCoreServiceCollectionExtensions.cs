using Huia.Endpoints;
using Huia.EntityFrameworkCore;
using Huia.EntityFrameworkCore.Stores;
using Huia.Identity;
using Huia.Keys;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the EF Core implementations of Huia storage abstractions.</summary>
public static class HuiaEntityFrameworkCoreServiceCollectionExtensions
{
    /// <summary>Registers <see cref="IHuiaSigningKeyStore"/> and <see cref="IHuiaAdminStore"/> backed by EF Core.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddHuiaEntityFrameworkCoreStores(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IHuiaSigningKeyStore, EfHuiaSigningKeyStore>();
        services.TryAddScoped<IHuiaAdminStore, EfHuiaAdminStore>();
        return services;
    }

    /// <summary>Registers base Huia EF Core stores for <typeparamref name="TContext"/>.</summary>
    /// <typeparam name="TContext">The context type deriving from <see cref="HuiaDbContext"/>.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddHuiaEntityFrameworkCore<TContext>(this IServiceCollection services)
        where TContext : HuiaDbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<HuiaDbContext>(sp => sp.GetRequiredService<TContext>());

        services.AddIdentityCore<HuiaUser>()
            .AddRoles<HuiaRole>()
            .AddEntityFrameworkStores<TContext>();

        return services.AddHuiaEntityFrameworkCoreStores();
    }

    /// <summary>Alias for <see cref="AddHuiaEntityFrameworkCoreStores"/>.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddHuiaEntityFrameworkCore(this IServiceCollection services)
        => services.AddHuiaEntityFrameworkCoreStores();
}
