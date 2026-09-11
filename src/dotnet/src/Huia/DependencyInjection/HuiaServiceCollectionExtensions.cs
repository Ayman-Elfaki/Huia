using Huia.Configuration;
using Huia.DependencyInjection;
using Huia.Emails;
using Huia.Eventing;
using Huia.HealthChecks;
using Huia.Identity;
using Huia.Keys;
using Huia.Localization;
using Huia.Options;
using Huia.Security;
using Huia.Services;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quartz;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Entry point for registering the core Huia services with the DI container.</summary>
public static class HuiaServiceCollectionExtensions
{
    /// <summary>
    /// Registers common Huia services (Multi-tenancy, Identity, Keys, Localization, Events, Emails, Phone/OTP).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the options tree; validated before anything is registered.</param>
    /// <returns>An <see cref="IHuiaBuilder"/> for feature opt-ins.</returns>
    /// <exception cref="HuiaOptionsException">The configured options are invalid.</exception>
    public static IHuiaBuilder AddHuia(this IServiceCollection services, Action<HuiaOptionsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var optionsBuilder = new HuiaOptionsBuilder();
        configure(optionsBuilder);
        var options = optionsBuilder.Build();

        services.AddSingleton(options);
        services.AddSingleton(Options.Options.Create(options));

        services.AddHuiaMultiTenancy(options);
        services.AddHuiaIdentity();
        services.AddHuiaAuthorization();
        services.AddHuiaPerTenantIdentityOptions(options);
        services.AddHuiaPerTenantPasskeyOptions(options);
        services.AddHuiaFlowIdentity(options);
        services.AddHuiaLocalization();
        services.AddHuiaEventing();
        services.AddHuiaKeyManagement(options);

        if (options.Keys.EnableBackgroundJobs || options.Cleanup.EnableBackgroundJobs)
        {
            services.AddQuartzHostedService(quartz =>
            {
                quartz.WaitForJobsToComplete = true;
                quartz.AwaitApplicationStarted = true;
            });
        }

        services.AddHostedService<HuiaRoleSeeder>();

        services.AddOptions<HuiaSecurityHeadersOptions>();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IPhoneNumberService, PhoneNumberService>();
        services.TryAddSingleton<ICountryCatalog, CountryCatalog>();
        services.TryAddSingleton<IOtpRateLimiter, InMemoryOtpRateLimiter>();
        services.TryAddSingleton<IPhoneLoginRateLimiter, InMemoryPhoneLoginRateLimiter>();
        services.TryAddSingleton<IPendingPhoneSignup, PendingPhoneSignup>();
        services.TryAddScoped<IOtpService, OtpService>();
        services.TryAddScoped<ISmsSender, HuiaSmsSender>();
        services.TryAddScoped<ICaptchaVerifier, NullCaptchaVerifier>();
        services.TryAddScoped<IHuiaEmailSender, HuiaEmailSender>();
        services.TryAddScoped<RazorEmailRenderer>();
        services.TryAddScoped<HuiaCspNonce>();
        services.TryAddScoped<IHuiaCspNonce>(sp => sp.GetRequiredService<HuiaCspNonce>());

        services.AddHuiaHealthChecks();
        services.AddPagination();
        services.AddDataProtection();
        services.AddMemoryCache();
        services.AddHybridCache();
        services.AddRouting();
        services.Configure<RouteOptions>(routing => routing.LowercaseUrls = true);

        return new HuiaBuilder(services, options);
    }

    /// <summary>
    /// Configures common response security headers (HSTS, nosniff, referrer, permissions policy).
    /// </summary>
    /// <param name="builder">The Huia builder.</param>
    /// <param name="configure">Optional action to configure security headers options.</param>
    /// <returns>The same builder for chaining.</returns>
    public static IHuiaBuilder AddHuiaSecurityHeaders(this IHuiaBuilder builder, Action<HuiaSecurityHeadersOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<HuiaSecurityHeadersMarker>();
        if (configure is not null)
        {
            builder.Services.Configure(configure);
        }

        return builder;
    }
}
