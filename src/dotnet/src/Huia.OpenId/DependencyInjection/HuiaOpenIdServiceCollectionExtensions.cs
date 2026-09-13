using Huia.DependencyInjection;
using Huia.OpenId.Configuration;
using Huia.OpenId.EntityFrameworkCore.Entities;
using Huia.OpenId.Flows;
using Huia.OpenId.HealthChecks;
using Huia.OpenId.Identity;
using Huia.OpenId.Keys;
using Huia.OpenId.OpenIddict;
using Huia.OpenId.Security;
using Huia.OpenId.Services;
using Huia.Options;
using Huia.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quartz;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Adds the multi-tenant, OpenIddict-backed flavor of Huia: Finbuckle multi-tenancy, per-tenant
/// identity/passkey options, the flow-specific sign-in options, the OpenIddict server/client, the
/// signing-key lifecycle, passwordless SMS + external login, and the readiness health check. The sole
/// entry point for an OpenId host — chain <c>.AddEntityFrameworkCoreStores&lt;HuiaDbContext, HuiaUser,
/// HuiaRole&gt;()</c>, then <c>.AddHuiaUi()</c> and/or <c>.AddHuiaSecurityHeaders()</c> for the Razor Pages
/// account UI and the opt-in security-headers middleware.
/// </summary>
public static class HuiaOpenIdServiceCollectionExtensions
{
    /// <summary>
    /// Registers the multi-tenant, OpenIddict-backed flavor of Huia.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the options tree; validated before anything is registered.</param>
    /// <returns>An <see cref="IHuiaBuilder"/> for feature opt-ins.</returns>
    /// <exception cref="HuiaOptionsException">The configured options are invalid.</exception>
    public static IHuiaBuilder AddHuiaOpenId(this IServiceCollection services, Action<HuiaOptionsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var optionsBuilder = new HuiaOptionsBuilder();
        configure(optionsBuilder);
        var options = optionsBuilder.Build();

        var builder = services.AddHuiaCore(options);

        services.AddHuiaMultiTenancy(options);

        // The cookie-based authentication scheme — AddEntityFrameworkCoreStores() deliberately stops short
        // of this (AddIdentityCore, not AddIdentity), since the scheme choice is flavor-specific (cookies
        // here; bearer tokens for Headless). Must run before AddHuiaPerTenantAuthentication(), which wraps
        // the schemes AddIdentityCookies() just registered.
        services.AddAuthentication(o =>
        {
            o.DefaultScheme = IdentityConstants.ApplicationScheme;
            o.DefaultSignInScheme = IdentityConstants.ExternalScheme;
        }).AddIdentityCookies();

        new IdentityBuilder(typeof(HuiaUser), typeof(HuiaRole), services)
            .AddUserManager<HuiaUserManager>()
            .AddSignInManager<HuiaSignInManager>();

        services.Configure<IdentityOptions>(identity =>
        {
            // Cross-tenant uniqueness is a composite index in HuiaDbContext; Identity's own check is
            // opt-in per tenant, so the baseline stays off.
            identity.User.RequireUniqueEmail = false;

            // Confirmed-email / confirmed-phone gating varies per flow, so it is applied to the named
            // per-flow options by AddHuiaFlowIdentity — never here, where one tenant's rule would leak
            // onto every flow-agnostic sign-in.
            identity.SignIn.RequireConfirmedEmail = false;
            identity.SignIn.RequireConfirmedAccount = false;
            identity.SignIn.RequireConfirmedPhoneNumber = false;
        });

        services.AddHuiaPerTenantAuthentication();
        services.AddHuiaPerTenantIdentityOptions(options);
        services.AddHuiaPerTenantPasskeyOptions(options);
        services.AddHuiaFlowIdentity(options);
        services.AddHuiaOpenIddict(options);
        services.AddHuiaKeyManagement(options);

        // Centralized here (rather than in AddHuiaKeyManagement/AddHuiaOpenIddict) because either flag
        // alone must be enough to actually run the scheduler that drives its own jobs.
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
        services.AddHostedService<HuiaRoleSeeder>();

        services.AddScoped<HuiaCspNonce>();
        services.AddScoped<IHuiaCspNonce>(sp => sp.GetRequiredService<HuiaCspNonce>());
        services.AddScoped<HuiaPasskeyRegistrar>();
        services.AddOptions<HuiaSecurityHeadersOptions>();

        // Passwordless SMS + shared flow services (always on; hosts replace the SMS / CAPTCHA senders).
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IPhoneNumberService, PhoneNumberService>();
        services.TryAddSingleton<ICountryCatalog, CountryCatalog>();
        services.TryAddSingleton<IOtpRateLimiter, InMemoryOtpRateLimiter>();
        services.TryAddSingleton<IPhoneLoginRateLimiter, InMemoryPhoneLoginRateLimiter>();
        services.TryAddSingleton<IPendingPhoneSignup, PendingPhoneSignup>();
        services.TryAddScoped<IOtpService<HuiaUser>, OtpService<HuiaUser>>();
        services.TryAddScoped<ISmsSender, HuiaSmsSender>();
        services.TryAddScoped<ICaptchaVerifier, NullCaptchaVerifier>();
        services.TryAddScoped<IReturnUrlProtector, ReturnUrlProtector>();

        services.AddHuiaAuthorization();
        services.AddHuiaHealthChecks();

        return builder;
    }

    /// <summary>
    /// Opts into the security-headers layer: a per-tenant Content-Security-Policy with a per-request
    /// nonce, plus HSTS (HTTPS only), <c>X-Content-Type-Options</c>, <c>Referrer-Policy</c> and
    /// <c>Permissions-Policy</c>. <c>UseHuiaOpenId()</c> inserts the middleware right after tenant resolution.
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
