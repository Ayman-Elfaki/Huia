using Huia.DependencyInjection;
using Huia.OpenId;
using Huia.OpenId.Configuration;
using Huia.OpenId.Identity;
using Huia.OpenId.OpenIddict;
using Huia.OpenId.Security;
using Huia.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenIddict.Validation.AspNetCore;
using Quartz;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Entry point for registering the Huia OpenID Connect identity provider with DI.</summary>
public static class HuiaOpenIdServiceCollectionExtensions
{
    /// <summary>
    /// Registers the OpenIddict server + client, Razor Pages account UI, passkeys, and external logins.
    /// </summary>
    /// <param name="builder">The Huia builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <exception cref="InvalidOperationException">If combined with Huia.Headless in the same host.</exception>
    public static IHuiaBuilder AddHuiaOpenId(this IHuiaBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Guard 1: DI-time mutual exclusivity guard with Huia.Headless
        if (builder.Services.Any(d => d.ServiceType.Name == "HuiaHeadlessMarker" || d.ServiceType.FullName == "Huia.Headless.HuiaHeadlessMarker"))
        {
            throw new InvalidOperationException(
                "AddHuiaOpenId() cannot be combined with AddHuiaHeadless() in the same host. " +
                "Huia.OpenId and Huia.Headless are independent identity surfaces — pick one.");
        }

        // Guard 3: EF Core DbContext mutual exclusivity guard
        if (builder.Services.Any(d => d.ServiceType.Name.Contains("HuiaHeadlessDbContext")))
        {
            throw new InvalidOperationException(
                "HuiaOpenIdDbContext cannot be combined with HuiaHeadlessDbContext in the same host.");
        }

        builder.Services.TryAddSingleton<HuiaOpenIdMarker>();

        var services = builder.Services;
        var options = builder.Options;

        services.AddHuiaCookieHardening(requireSecure: !options.DisableTransportSecurityRequirement);
        services.AddHuiaPerTenantAuthentication();
        services.AddHuiaOpenIddict(options);

        // Forward HuiaConstants.Schemes.Api to OpenIddict validation
        services.AddAuthentication()
            .AddPolicyScheme(HuiaConstants.Schemes.Api, null, schemeOptions =>
            {
                schemeOptions.ForwardDefault = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
            });

        if (options.Keys.EnableBackgroundJobs || options.Cleanup.EnableBackgroundJobs)
        {
            services.AddQuartzHostedService(quartz =>
            {
                quartz.WaitForJobsToComplete = true;
                quartz.AwaitApplicationStarted = true;
            });
        }

        services.AddHostedService<HuiaClientSeeder>();
        services.AddHostedService<HuiaScopeSeeder>();

        services.AddSingleton<IHuiaFormActionOriginsProvider, HuiaOpenIdFormActionOriginsProvider>();
        services.AddSingleton<Huia.HealthChecks.IHuiaReadinessContributor, Huia.OpenId.HealthChecks.HuiaOpenIdReadinessContributor>();
        services.AddScoped<HuiaPasskeyRegistrar>();

        return builder;
    }

    /// <summary>
    /// Registers common Huia and OpenID Connect services in a single call.
    /// </summary>
    public static IHuiaBuilder AddHuiaOpenId(this IServiceCollection services, Action<HuiaOptionsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        return services.AddHuia(configure).AddHuiaOpenId();
    }
}
