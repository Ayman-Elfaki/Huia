using Huia.DependencyInjection;
using Huia.Entities;
using Huia.Headless.Identity;
using Huia.Headless.Multitenancy;
using Huia.Headless.Services;
using Huia.Identity;
using Huia.Multitenancy;
using Huia.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers the single-tenant, bearer-token flavor of Huia: ASP.NET Core Identity's own API endpoints
/// (<c>AddApiEndpoints</c> / <c>MapIdentityApi</c> — register, login, refresh, email confirmation,
/// password reset, 2FA, profile) plus passkeys and passwordless SMS phone login, on the shared
/// <see cref="HuiaUserManager{TUser}"/> / <see cref="HuiaSignInManager{TUser}"/> /
/// <see cref="HuiaPasskeyRegistrar{TUser}"/> core. No multi-tenancy, no OpenIddict — call
/// <c>.AddHuiaOpenId()</c> from <c>Huia.OpenId</c> instead for that.
/// </summary>
public static class HuiaHeadlessServiceCollectionExtensions
{
    /// <summary>
    /// Registers the single-tenant flavor of Huia. Call after <c>AddHuia()</c> and
    /// <c>.AddEntityFrameworkCoreStores&lt;TContext&gt;()</c>.
    /// </summary>
    /// <param name="builder">The Huia builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <exception cref="InvalidOperationException">The host configured anything other than exactly one tenant.</exception>
    public static IHuiaBuilder AddHuiaHeadless(this IHuiaBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;
        var options = builder.Options;

        if (options.Tenants.Count != 1)
        {
            throw new InvalidOperationException(
                "Huia.Headless is single-tenant: configure exactly one tenant with AddTenant(...). " +
                $"{options.Tenants.Count} were configured.");
        }

        var tenantId = options.Tenants.Keys.Single();
        services.AddSingleton<IHuiaTenantContext>(new HuiaSingleTenantContext(tenantId));
        services.AddHttpContextAccessor();

        // Bearer tokens (ASP.NET Core Identity's own scheme), not cookies — MapIdentityApi issues and
        // validates these directly; there is no interactive sign-in UI to protect with a cookie.
        services.AddAuthentication(IdentityConstants.BearerScheme)
            .AddBearerToken(IdentityConstants.BearerScheme);
        services.AddAuthorization();

        new IdentityBuilder(typeof(HuiaUser), typeof(HuiaRole), services)
            .AddApiEndpoints()
            .AddUserManager<HuiaUserManager>()
            .AddSignInManager<HuiaSignInManager<HuiaUser>>();

        // AddUserManager<HuiaUserManager>() bridges UserManager<HuiaUser> to the concrete type, but not
        // the generic Huia.Identity.HuiaUserManager<HuiaUser> in between — HuiaPasskeyRegistrar<TUser>
        // depends on that directly (for the phone/external members, not just the ASP.NET Core surface).
        services.AddScoped<Huia.Identity.HuiaUserManager<HuiaUser>>(sp => sp.GetRequiredService<HuiaUserManager>());
        services.AddScoped<HuiaPasskeyRegistrar<HuiaUser>>();

        // Passwordless SMS (always on; hosts replace the SMS / CAPTCHA senders) — the same core
        // services Huia.OpenId registers, shared rather than duplicated.
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IPhoneNumberService, PhoneNumberService>();
        services.TryAddSingleton<IOtpRateLimiter, InMemoryOtpRateLimiter>();
        services.TryAddSingleton<IPhoneLoginRateLimiter, InMemoryPhoneLoginRateLimiter>();
        services.TryAddSingleton<IPendingPhoneSignup, PendingPhoneSignup>();
        services.TryAddSingleton<IPhoneLoginFlowStore, PhoneLoginFlowStore>();
        services.TryAddScoped<IOtpService<HuiaUser>, OtpService<HuiaUser>>();
        services.TryAddScoped<ISmsSender, HuiaSmsSender>();
        services.TryAddScoped<ICaptchaVerifier, NullCaptchaVerifier>();

        return builder;
    }
}
