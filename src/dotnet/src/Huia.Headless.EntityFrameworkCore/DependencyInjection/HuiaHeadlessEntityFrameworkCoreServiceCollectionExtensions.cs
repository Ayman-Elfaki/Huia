using Huia.Headless.EntityFrameworkCore.Stores;
using Huia.Headless.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Huia.Headless.EntityFrameworkCore.DependencyInjection;

/// <summary>
/// Extension methods for setting up Huia Headless Entity Framework Core services in an <see cref="IServiceCollection"/>.
/// </summary>
public static class HuiaHeadlessEntityFrameworkCoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers the EF Core refresh token store for Huia Headless against the specified <typeparamref name="TContext"/>.
    /// Enforces mutual exclusivity with Huia OpenID DbContext.
    /// </summary>
    /// <typeparam name="TContext">The concrete <see cref="HuiaHeadlessDbContext"/>.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddHuiaHeadlessEntityFrameworkCore<TContext>(this IServiceCollection services)
        where TContext : HuiaHeadlessDbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        // Guard 3: Mutual exclusivity at EF / DbContext level
        if (services.Any(sd =>
            sd.ServiceType.FullName?.Contains("HuiaOpenIdDbContext", StringComparison.Ordinal) == true ||
            sd.ImplementationType?.FullName?.Contains("HuiaOpenIdDbContext", StringComparison.Ordinal) == true))
        {
            throw new InvalidOperationException(
                "Cannot register Huia Headless Entity Framework Core alongside Huia OpenID Entity Framework Core. " +
                "Choose exactly one flavor per host.");
        }

        services.TryAddScoped<IHuiaHeadlessRefreshTokenStore, EfHuiaHeadlessRefreshTokenStore<TContext>>();

        if (typeof(TContext) != typeof(HuiaHeadlessDbContext))
        {
            services.TryAddScoped<HuiaHeadlessDbContext>(sp => sp.GetRequiredService<TContext>());
        }

        return services;
    }

    /// <summary>
    /// Registers the EF Core refresh token store using the default <see cref="HuiaHeadlessDbContext"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddHuiaHeadlessEntityFrameworkCore(this IServiceCollection services)
    {
        return services.AddHuiaHeadlessEntityFrameworkCore<HuiaHeadlessDbContext>();
    }
}
