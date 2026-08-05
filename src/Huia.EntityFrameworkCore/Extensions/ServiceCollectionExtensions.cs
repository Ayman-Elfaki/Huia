using Huia.Core;
using Huia.EntityFrameworkCore.Common;
using Huia.EntityFrameworkCore.Keys;
using Huia.EntityFrameworkCore.Sessions;
using Huia.Identity;
using Huia.Keys;
using Huia.Sessions;
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
    public static HuiaBuilder WithEntityFrameworkStores<TContext>(
        this HuiaBuilder builder,
        Action<IServiceCollectionQuartzConfigurator>? configureQuartz = null,
        Action<QuartzHostedServiceOptions>? configureHostedService = null)
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
        services.AddScoped<IUserSessionManager, EfCoreUserSessionManager<TContext>>();

        services.AddOpenIddict()
            .AddCore(options =>
            {
                options.UseEntityFrameworkCore()
                    .UseDbContext<TContext>();

                options.UseQuartz();
            });

        services.AddQuartz(configureQuartz);

        // Hourly is frequent enough that a revoked/expired session's row doesn't linger long, but coarse
        // enough not to matter that it isn't tied to AbsoluteLifetime precisely — sessions are also already
        // treated as dead by HuiaUserSessionService.IsLiveAsync well before this ever runs; this job only
        // ever cleans up rows nothing is relying on anymore.
        services.AddQuartz(quartz =>
        {
            var jobKey = new JobKey("Huia.UserSessionPruning");
            quartz.AddJob<HuiaUserSessionPruningJob<TContext>>(job => job.WithIdentity(jobKey).StoreDurably());
            quartz.AddTrigger(trigger => trigger
                .ForJob(jobKey)
                .WithIdentity("Huia.UserSessionPruning-trigger")
                .WithSimpleSchedule(schedule => schedule.WithIntervalInHours(1).RepeatForever()));
        });

        services.AddQuartzHostedService(options =>
        {
            options.WaitForJobsToComplete = true;
            configureHostedService?.Invoke(options);
        });

        return builder;
    }
}