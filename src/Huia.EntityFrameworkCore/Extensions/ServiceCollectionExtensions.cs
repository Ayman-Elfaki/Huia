using Huia.Core;
using Huia.EntityFrameworkCore.Common;
using Huia.EntityFrameworkCore.Keys;
using Huia.Identity;
using Huia.Keys;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quartz;

namespace Huia.EntityFrameworkCore.Extensions;

/// <summary>
/// Registers EF Core-backed persistence for Huia's Identity, OpenIddict, and key-management stores.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Wires EF Core-backed persistence into a Huia app: ASP.NET Core Identity stores, OpenIddict's EF Core
    /// application/authorization/scope/token stores (with Quartz-based pruning of orphaned records), and
    /// the EF Core-backed <see cref="ISigningKeyStore"/> that backs key management (automatic or
    /// manual). Call this after <c>services.AddHuia(...)</c> and after registering
    /// <typeparamref name="TContext"/> with <c>AddDbContext</c>.
    /// </summary>
    /// <remarks>
    /// <typeparamref name="TContext"/> doesn't have to inherit <see cref="HuiaDbContext"/> — any
    /// <see cref="Microsoft.EntityFrameworkCore.DbContext"/> works, as long as its model includes
    /// <c>builder.UseOpenIddict()</c> and <c>builder.UseHuiaKeyManagement()</c> (both are called for you
    /// automatically if you do inherit <see cref="HuiaDbContext"/>). To back key management with a store
    /// other than EF Core entirely (e.g. MongoDB), skip this method and register your own
    /// <see cref="ISigningKeyStore"/> implementation instead — it's the provider-agnostic extension
    /// point and has no dependency on EF Core.
    /// </remarks>
    /// <param name="builder">The builder returned by <c>services.AddHuia(...)</c>.</param>
    /// <param name="configureQuartz">
    /// Customizes the underlying Quartz scheduler (e.g. a persistent job store for multi-instance
    /// deployments, thread pool size, misfire thresholds) used for OpenIddict's own record pruning and, if
    /// automatic key rotation is enabled, its rotation job.
    /// </param>
    /// <param name="configureHostedService">
    /// Customizes Quartz's hosted service. <see cref="QuartzHostedServiceOptions.WaitForJobsToComplete"/>
    /// defaults to <see langword="true"/>; this runs after that default, so it can override it.
    /// </param>
    /// <param name="entityCacheLimit">
    /// The maximum number of entities (applications, authorizations, scopes and tokens combined) OpenIddict's
    /// managers keep in their scoped, in-memory <c>IOpenIddictXxxCache</c> before evicting the oldest entries.
    /// OpenIddict's own default is 250, which a real deployment blows through almost immediately (every active
    /// authorization and every access/refresh token pair counts against it), turning most manager lookups back
    /// into database round-trips. Raised here so the caching OpenIddict already wires up by default is actually
    /// effective; pass a smaller value only for constrained/low-memory hosts.
    /// </param>
    public static HuiaBuilder WithEntityFrameworkStores<TContext>(
        this HuiaBuilder builder,
        Action<IServiceCollectionQuartzConfigurator>? configureQuartz = null,
        Action<QuartzHostedServiceOptions>? configureHostedService = null,
        int entityCacheLimit = 4_000)
        where TContext : DbContext
    {
        var services = builder.Services;

        new IdentityBuilder(typeof(HuiaUser), typeof(HuiaRole), services)
            .AddEntityFrameworkStores<TContext>()
            .AddDefaultTokenProviders();

        // EntityFrameworkCoreSigningKeyStore needs a HuiaKeyManagementOptions (e.g. for RsaKeySizeInBits)
        // regardless of which key management mode is enabled; TryAddSingleton leaves an already-configured
        // instance from huia.KeysManagement.UseAutomaticKeyManagement(...)/UseManualKeyManagement(...)
        // (configured inside AddHuia, so it always runs before this) alone.
        services.TryAddSingleton(new KeyManagementOptions());
        services.AddScoped<ISigningKeyStore, EfCoreSigningKeyStore<TContext>>();

        services.AddOpenIddict()
            .AddCore(options =>
            {
                options.UseEntityFrameworkCore()
                    .UseDbContext<TContext>();

                options.UseQuartz();

                // See the entityCacheLimit parameter doc comment above: this is what actually makes
                // OpenIddict's default (but too-small-to-matter) entity caching worth having.
                options.SetEntityCacheLimit(entityCacheLimit);
            });

        services.AddQuartz(configureQuartz);

        services.AddQuartzHostedService(options =>
        {
            options.WaitForJobsToComplete = true;
            configureHostedService?.Invoke(options);
        });

        return builder;
    }
}