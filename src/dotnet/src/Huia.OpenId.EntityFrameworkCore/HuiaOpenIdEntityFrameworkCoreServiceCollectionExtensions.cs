using Huia.OpenId.EntityFrameworkCore;
using Huia.OpenId.EntityFrameworkCore.Stores;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenIddict.Abstractions;
using OpenIddict.Core;
using OpenIddict.EntityFrameworkCore.Models;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Extension methods for registering Huia OpenID Entity Framework Core stores.</summary>
public static class HuiaOpenIdEntityFrameworkCoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers the tenant-scoped OpenIddict stores and base Huia EF Core stores for <typeparamref name="TContext"/>.
    /// </summary>
    /// <typeparam name="TContext">The context type deriving from <see cref="HuiaOpenIdDbContext"/>.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddHuiaOpenIdEntityFrameworkCore<TContext>(this IServiceCollection services)
        where TContext : HuiaOpenIdDbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHuiaEntityFrameworkCore<TContext>();

        services.AddOpenIddict()
            .AddCore(core =>
            {
                core.UseEntityFrameworkCore().UseDbContext<TContext>();
            });

        services.Replace(ServiceDescriptor.Scoped(
            typeof(IOpenIddictApplicationStore<OpenIddictEntityFrameworkCoreApplication>),
            typeof(HuiaOpenIddictApplicationStore)));

        services.Replace(ServiceDescriptor.Scoped(
            typeof(IOpenIddictScopeStore<OpenIddictEntityFrameworkCoreScope>),
            typeof(HuiaOpenIddictScopeStore)));

        return services;
    }
}
