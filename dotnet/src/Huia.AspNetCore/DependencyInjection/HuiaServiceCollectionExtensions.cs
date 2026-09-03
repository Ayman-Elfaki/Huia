using Huia.AspNetCore.Configuration;
using Huia.AspNetCore.DependencyInjection;
using Huia.AspNetCore.Flows;
using Huia.AspNetCore.HealthChecks;
using Huia.AspNetCore.Keys;
using Huia.AspNetCore.Localization;
using Huia.AspNetCore.OpenIddict;
using Huia.AspNetCore.Security;
using Huia.AspNetCore.Services;
using Huia.EntityFrameworkCore;
using Huia.Options;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Entry point for registering the Huia identity provider with the DI container.</summary>
public static class HuiaServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Huia identity provider. The host must have already registered
    /// <see cref="HuiaDbContext"/> with a concrete provider — Huia ships no provider and no migrations.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the options tree; validated before anything is registered.</param>
    /// <returns>An <see cref="IHuiaBuilder"/> for feature opt-ins.</returns>
    /// <exception cref="InvalidOperationException"><see cref="HuiaDbContext"/> was not registered first.</exception>
    /// <exception cref="HuiaOptionsException">The configured options are invalid.</exception>
    public static IHuiaBuilder AddHuia(this IServiceCollection services, Action<HuiaOptionsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var optionsBuilder = new HuiaOptionsBuilder();
        configure(optionsBuilder);
        var options = optionsBuilder.Build();

        if (services.All(d => d.ServiceType != typeof(DbContextOptions<HuiaDbContext>)))
        {
            throw new InvalidOperationException(
                "HuiaDbContext must be registered before AddHuia is called, for example " +
                "services.AddDbContext<HuiaDbContext>(o => o.UseNpgsql(connectionString).UseOpenIddict()). " +
                "Huia ships no Entity Framework Core provider.");
        }

        services.AddSingleton(options);
        services.AddSingleton<IOptions<HuiaOptions>>(Options.Options.Create(options));

        services.AddHuiaMultiTenancy(options);
        services.AddHuiaIdentity(options);
        services.AddHuiaCookieHardening(requireSecure: !options.DisableTransportSecurityRequirement);
        services.AddHuiaPerTenantAuthentication();
        services.AddHuiaPerTenantIdentityOptions(options);
        services.AddHuiaLocalization();
        services.AddHuiaEventing();
        services.AddHuiaOpenIddict(options);
        services.AddHuiaKeyManagement(options);
        services.AddHostedService<Huia.AspNetCore.OpenIddict.HuiaClientSeeder>();
        services.AddHostedService<Huia.AspNetCore.OpenIddict.HuiaScopeSeeder>();

        services.AddScoped<HuiaCspNonce>();
        services.AddScoped<IHuiaCspNonce>(sp => sp.GetRequiredService<HuiaCspNonce>());
        services.AddOptions<HuiaSecurityHeadersOptions>();

        // Passwordless SMS + shared flow services (always on; hosts replace the SMS / CAPTCHA senders).
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IPhoneNumberService, PhoneNumberService>();
        services.TryAddSingleton<ICountryCatalog, CountryCatalog>();
        services.TryAddSingleton<IOtpRateLimiter, InMemoryOtpRateLimiter>();
        services.TryAddSingleton<IPhoneLoginRateLimiter, InMemoryPhoneLoginRateLimiter>();
        services.TryAddSingleton<IPendingPhoneSignup, PendingPhoneSignup>();
        services.TryAddScoped<IOtpService, OtpService>();
        services.TryAddScoped<ISmsSender, HuiaSmsSender>();
        services.TryAddScoped<ICaptchaVerifier, NullCaptchaVerifier>();
        services.TryAddScoped<IReturnUrlProtector, ReturnUrlProtector>();

        services.AddDataProtection();
        services.AddMemoryCache();
        services.AddHybridCache();
        services.AddRouting();
        services.Configure<RouteOptions>(routing =>
        {
            routing.LowercaseUrls = true;
            routing.LowercaseQueryStrings = false;
        });
        services.AddHuiaAuthorization();
        services.AddHuiaHealthChecks();

        return new HuiaBuilder(services, options);
    }

    /// <summary>
    /// Opts into the security-headers layer: a per-tenant Content-Security-Policy with a per-request
    /// nonce, plus HSTS (HTTPS only), <c>X-Content-Type-Options</c>, <c>Referrer-Policy</c> and
    /// <c>Permissions-Policy</c>. <c>UseHuia()</c> inserts the middleware right after tenant resolution.
    /// </summary>
    /// <param name="builder">The Huia builder.</param>
    /// <param name="configure">Optional tuning of the emitted policy.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IHuiaBuilder AddHuiaSecurityHeaders(this IHuiaBuilder builder, Action<HuiaSecurityHeadersOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddSingleton<HuiaSecurityHeadersMarker>();
        if (configure is not null)
        {
            builder.Services.Configure(configure);
        }

        return builder;
    }
}
