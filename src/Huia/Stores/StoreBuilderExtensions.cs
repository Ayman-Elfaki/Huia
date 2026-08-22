using Huia.Keys;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace Huia.Stores;

/// <summary>
/// Registers a fully custom (non-EF-Core) <see cref="IHuiaStore{TApplication,TAuthorization,TScope,TToken}"/>.
/// </summary>
public static class StoreBuilderExtensions
{
    /// <summary>
    /// Backs ASP.NET Core Identity and OpenIddict's persistence — plus signing/encryption key storage — with
    /// a single custom <typeparamref name="TStore"/> implementation, instead of
    /// <c>.WithEntityFrameworkStores&lt;TContext&gt;()</c>. Use this to store everything with something
    /// other than EF Core (e.g. Dapper). Key management (automatic or manual) still needs to be enabled
    /// separately via <c>huia.KeysManagement.UseAutomaticKeyManagement()</c>/<c>UseManualKeyManagement()</c>
    /// inside <c>AddHuia</c> to actually be used, but <typeparamref name="TStore"/> is registered as its
    /// <see cref="ISigningKeyStore"/> either way. See docs/custom-store.md and docs/key-management.md.
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

        services.AddScoped<IHuiaUserStore, TStore>();
        services.AddScoped<IHuiaUserLoginStore, TStore>();
        services.AddScoped<IHuiaUserRoleStore, TStore>();

        // Needed directly by TotpHuiaTokenProvider (not just reachable via HuiaUserManager's own cast of its
        // injected IHuiaUserStore), so it has to be independently resolvable from DI.
        services.AddScoped<IHuiaUserTokenStore, TStore>();
        services.AddScoped<IHuiaRoleStore, TStore>();
        services.AddScoped<ISigningKeyStore, TStore>();

        // A plain Add (not TryAdd) so it wins over the ThrowingPhoneNumberStore AddHuia registers by default
        // when huia.Authentication.UsePasswordlessFlow() is enabled — see the matching comment in
        // Huia.EntityFrameworkCore's WithEntityFrameworkStores.
        services.AddScoped<IHuiaPhoneNumberStore, TStore>();

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

        // Starts Quartz's hosted service for OpenIddict's own record pruning.
        // KeysManagement.UseAutomaticKeyManagement only calls AddQuartz(), not AddQuartzHostedService()
        // itself, so this is also what starts the rotation job's hosted service if you use that too.
        services.AddQuartz(configureQuartz);

        services.AddQuartzHostedService(options =>
        {
            options.WaitForJobsToComplete = true;
            configureHostedService?.Invoke(options);
        });

        return builder;
    }
}