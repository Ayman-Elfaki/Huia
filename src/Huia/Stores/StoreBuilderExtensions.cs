using Huia.Core;
using Huia.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace Huia.Stores;

/// <summary>
/// Registers a fully custom (non-EF-Core) <see cref="IHuiaStore{TApplication,TAuthorization,TScope,TToken}"/>.
/// </summary>
public static class StoreBuilderExtensions
{
    /// <summary>
    /// Backs ASP.NET Core Identity and OpenIddict's persistence with a single custom
    /// <typeparamref name="TStore"/> implementation, instead of
    /// <c>.WithEntityFrameworkStores&lt;TContext&gt;()</c>. Use this to store everything with something
    /// other than EF Core (e.g. Dapper). Says nothing about signing-key storage — if you also want automatic
    /// or manual key management, additionally call <c>.WithSigningKeyStore&lt;TKeyStore&gt;()</c>
    /// (<typeparamref name="TStore"/> can implement that too, or use a separate type), and configure
    /// <c>huia.KeysManagement.UseAutomaticKeyManagement()</c>/<c>UseManualKeyManagement()</c> inside
    /// <c>AddHuia</c>. See docs/custom-store.md and docs/key-management.md.
    /// </summary>
    /// <param name="builder">The builder returned by <c>services.AddHuia(...)</c>.</param>
    /// <param name="configureQuartz">
    /// Customizes the underlying Quartz scheduler (e.g. a persistent job store for multi-instance
    /// deployments, thread pool size, misfire thresholds) used for OpenIddict's own record pruning.
    /// </param>
    /// <param name="configureHostedService">
    /// Customizes Quartz's hosted service. <see cref="QuartzHostedServiceOptions.WaitForJobsToComplete"/>
    /// defaults to <see langword="true"/>; this runs after that default, so it can override it.
    /// </param>
    public static HuiaBuilder WithStore<TStore, TApplication, TAuthorization, TScope, TToken>(this HuiaBuilder builder,
        Action<IServiceCollectionQuartzConfigurator>? configureQuartz = null,
        Action<QuartzHostedServiceOptions>? configureHostedService = null)
        where TStore : class, IHuiaStore<TApplication, TAuthorization, TScope, TToken>
        where TApplication : class
        where TAuthorization : class
        where TScope : class
        where TToken : class
    {
        var services = builder.Services;

        services.AddScoped<IUserStore<HuiaUser>, TStore>();
        services.AddScoped<IRoleStore<HuiaRole>, TStore>();

        services.AddOpenIddict()
            .AddCore(core =>
            {
                core.SetDefaultApplicationEntity<TApplication>();
                core.SetDefaultAuthorizationEntity<TAuthorization>();
                core.SetDefaultScopeEntity<TScope>();
                core.SetDefaultTokenEntity<TToken>();

                core.ReplaceApplicationStore<TApplication, TStore>();
                core.ReplaceAuthorizationStore<TAuthorization, TStore>();
                core.ReplaceScopeStore<TScope, TStore>();
                core.ReplaceTokenStore<TToken, TStore>();
            });

        // Starts Quartz's hosted service for OpenIddict's own record pruning. WithSigningKeyStore/
        // KeysManagement.UseAutomaticKeyManagement only call AddQuartz(), not AddQuartzHostedService()
        // themselves, so this is also what starts the rotation job's hosted service if you use those too.
        services.AddQuartz(configureQuartz);

        services.AddQuartzHostedService(options =>
        {
            options.WaitForJobsToComplete = true;
            configureHostedService?.Invoke(options);
        });

        return builder;
    }
}