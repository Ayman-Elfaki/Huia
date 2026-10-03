using Finbuckle.MultiTenant;
using Finbuckle.MultiTenant.AspNetCore.Extensions;
using Finbuckle.MultiTenant.Extensions;
using Huia.Multitenancy;
using Huia.OpenId.DependencyInjection;
using Huia.OpenId.EntityFrameworkCore.Multitenancy;
using Huia.OpenId.Multitenancy;
using Huia.Options;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Huia.OpenId.Configuration;

/// <summary>
/// Wires Finbuckle with the base-path strategy and an in-memory store populated from the configured
/// tenants, plus per-tenant authentication so an interactive login session is bound to the tenant it
/// was created under.
/// </summary>
internal static class HuiaMultiTenancyConfiguration
{
    public static IServiceCollection AddHuiaMultiTenancy(
        this IServiceCollection services, HuiaOptions options, HuiaOpenIdConfigurationBuilder? configBuilder = null)
    {
        var tenants = options.Tenants.Keys.Select(id => new HuiaTenantInfo(id)).ToList();

        var builder = services.AddMultiTenant<HuiaTenantInfo>()
            .WithBasePathStrategy(strategy => strategy.RebaseAspNetCorePathBase = true)
            .WithInMemoryStore(store =>
            {
                store.IsCaseSensitive = true;
                foreach (var tenant in tenants)
                {
                    store.Tenants.Add(tenant);
                }
            });

        if (configBuilder is not null)
        {
            foreach (var configure in configBuilder.FinbuckleConfigurations)
            {
                configure(builder);
            }
        }

        services.AddHttpContextAccessor();
        services.AddScoped<IHuiaTenantContext, HuiaFinbuckleTenantContext>();

        return services;
    }

    /// <summary>
    /// Enables Finbuckle per-tenant authentication and gives each tenant its own cookie name. Must run
    /// <em>after</em> <c>AddHuiaIdentity</c> (which registers <see cref="Microsoft.AspNetCore.Authentication.IAuthenticationService"/>)
    /// and after <c>AddHuiaCookieHardening</c> (so the per-tenant name overrides the shared one).
    /// </summary>
    public static IServiceCollection AddHuiaPerTenantAuthentication(this IServiceCollection services)
    {
        new MultiTenantBuilder<HuiaTenantInfo>(services).WithPerTenantAuthentication();

        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .ConfigurePerTenant<CookieAuthenticationOptions, HuiaTenantInfo>((cookie, tenant) =>
                cookie.Cookie.Name = $"{HuiaConstants.Cookies.Authentication}.{tenant.Identifier}");

        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.TwoFactorRememberMeScheme)
            .ConfigurePerTenant<CookieAuthenticationOptions, HuiaTenantInfo>((cookie, tenant) =>
                cookie.Cookie.Name = $"huia.2fa.{tenant.Identifier}");

        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.TwoFactorUserIdScheme)
            .ConfigurePerTenant<CookieAuthenticationOptions, HuiaTenantInfo>((cookie, tenant) =>
                cookie.Cookie.Name = $"{HuiaConstants.Cookies.TwoFactorUser}.{tenant.Identifier}");

        return services;
    }

    /// <summary>
    /// Projects each tenant's passkey policy onto <see cref="IdentityPasskeyOptions"/>.
    /// </summary>
    public static IServiceCollection AddHuiaPerTenantPasskeyOptions(this IServiceCollection services, HuiaOptions options)
    {
        services.Configure<IdentityPasskeyOptions>(identity => identity.ResidentKeyRequirement = "required");

        services.AddOptions<IdentityPasskeyOptions>()
            .ConfigurePerTenant<IdentityPasskeyOptions, HuiaTenantInfo>((passkey, tenant) =>
            {
                passkey.ResidentKeyRequirement = "required";

                if (!options.Tenants.TryGetValue(tenant.Identifier, out var config) || config.Authentication.Find<PasskeyAuthenticationMethod>() is not { } policy)
                {
                    return;
                }

                passkey.UserVerificationRequirement = policy.UserVerification switch
                {
                    PasskeyUserVerification.Required => "required",
                    PasskeyUserVerification.Discouraged => "discouraged",
                    _ => "preferred",
                };
                passkey.AuthenticatorAttachment = policy.AuthenticatorAttachment switch
                {
                    PasskeyAuthenticatorAttachment.Platform => "platform",
                    PasskeyAuthenticatorAttachment.CrossPlatform => "cross-platform",
                    _ => null,
                };
                passkey.AuthenticatorTimeout = policy.AuthenticatorTimeout;

                // Per-tenant relying-party id. Left unset the framework uses Request.Host.Host.
                if (!string.IsNullOrWhiteSpace(policy.RelyingPartyId))
                {
                    passkey.ServerDomain = policy.RelyingPartyId;
                }

                // Per-tenant allowed origins. Left empty the framework's default same-origin check applies.
                if (policy.AllowedOrigins.Count > 0)
                {
                    var allowed = policy.AllowedOrigins
                        .Select(o => new Uri(o, UriKind.Absolute).GetLeftPart(UriPartial.Authority))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    passkey.ValidateOrigin = context =>
                    {
                        if (string.IsNullOrEmpty(context.Origin) || !Uri.TryCreate(context.Origin, UriKind.Absolute, out var origin))
                        {
                            return ValueTask.FromResult(false);
                        }

                        if (allowed.Contains(origin.GetLeftPart(UriPartial.Authority)))
                        {
                            return ValueTask.FromResult(true);
                        }

                        var requestOrigin = context.HttpContext.Request.Headers.Origin.ToString();
                        return ValueTask.FromResult(!context.CrossOrigin && string.Equals(requestOrigin, context.Origin, StringComparison.Ordinal));
                    };
                }
            });

        services.AddScoped<IOptions<IdentityPasskeyOptions>>(sp =>
            new OptionsWrapper<IdentityPasskeyOptions>(sp.GetRequiredService<IOptionsSnapshot<IdentityPasskeyOptions>>().Value));

        return services;
    }

    /// <summary>
    /// Projects each tenant's password-complexity policy (and the opt-in unique-email rule) onto the
    /// default <see cref="IdentityOptions"/> for that tenant.
    /// </summary>
    public static IServiceCollection AddHuiaPerTenantIdentityOptions(this IServiceCollection services, HuiaOptions options)
    {
        services.AddOptions<IdentityOptions>()
            .ConfigurePerTenant<IdentityOptions, HuiaTenantInfo>((identity, tenant) =>
            {
                if (!options.Tenants.TryGetValue(tenant.Identifier, out var config))
                {
                    return;
                }

                var emailAndPassword = config.Authentication.Find<EmailPasswordAuthenticationMethod>() ?? new EmailPasswordAuthenticationMethod();
                identity.Password.RequiredLength = emailAndPassword.MinimumLength;
                identity.Password.RequireDigit = emailAndPassword.RequireDigit;
                identity.Password.RequireLowercase = emailAndPassword.RequireLowercase;
                identity.Password.RequireUppercase = emailAndPassword.RequireUppercase;
                identity.Password.RequireNonAlphanumeric = emailAndPassword.RequireNonAlphanumeric;
                identity.Password.RequiredUniqueChars = emailAndPassword.RequiredUniqueChars;
                identity.User.RequireUniqueEmail = emailAndPassword.RequireUniqueEmail;
            });

        services.AddScoped<IOptions<IdentityOptions>>(sp =>
            new OptionsWrapper<IdentityOptions>(sp.GetRequiredService<IOptionsSnapshot<IdentityOptions>>().Value));

        return services;
    }
}
