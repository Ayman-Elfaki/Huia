using Finbuckle.MultiTenant;
using Finbuckle.MultiTenant.AspNetCore.Extensions;
using Finbuckle.MultiTenant.Extensions;
using Huia.EntityFrameworkCore.Multitenancy;
using Huia.Options;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Huia.AspNetCore.Configuration;

/// <summary>
/// Wires Finbuckle with the base-path strategy and an in-memory store populated from the configured
/// tenants, plus per-tenant authentication so an interactive login session is bound to the tenant it
/// was created under.
/// </summary>
internal static class HuiaMultiTenancyConfiguration
{
    public static IServiceCollection AddHuiaMultiTenancy(this IServiceCollection services, HuiaOptions options)
    {
        var tenants = options.Tenants.Keys.Select(id => new HuiaTenantInfo(id)).ToList();

        services.AddMultiTenant<HuiaTenantInfo>()
            .WithBasePathStrategy(strategy => strategy.RebaseAspNetCorePathBase = true)
            .WithInMemoryStore(store =>
            {
                store.IsCaseSensitive = true;
                foreach (var tenant in tenants)
                {
                    store.Tenants.Add(tenant);
                }
            });

        services.AddHttpContextAccessor();

        return services;
    }

    /// <summary>
    /// Enables Finbuckle per-tenant authentication and gives each tenant its own cookie name. Must run
    /// <em>after</em> <c>AddHuiaIdentity</c> (which registers <see cref="Microsoft.AspNetCore.Authentication.IAuthenticationService"/>)
    /// and after <c>AddHuiaCookieHardening</c> (so the per-tenant name overrides the shared one).
    /// </summary>
    /// <remarks>
    /// Finbuckle's <c>WithPerTenantAuthentication()</c>
    /// wraps <c>OnValidatePrincipal</c> for every cookie scheme: a ticket carries the tenant it was
    /// minted under in its (encrypted) authentication properties, and a request whose resolved tenant
    /// does not match has its principal rejected. The per-tenant cookie <em>name</em> is layered on top
    /// so a browser can hold several tenants' sessions at once.
    /// </remarks>
    public static IServiceCollection AddHuiaPerTenantAuthentication(this IServiceCollection services)
    {
        new MultiTenantBuilder<HuiaTenantInfo>(services).WithPerTenantAuthentication();

        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .ConfigurePerTenant<CookieAuthenticationOptions, HuiaTenantInfo>((cookie, tenant) =>
                cookie.Cookie.Name = $"{HuiaConstants.Cookies.Authentication}.{tenant.Identifier}");

        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.TwoFactorRememberMeScheme)
            .ConfigurePerTenant<CookieAuthenticationOptions, HuiaTenantInfo>((cookie, tenant) =>
                cookie.Cookie.Name = $"huia.2fa.{tenant.Identifier}");

        // ASP.NET Core Identity's passkey helpers stash the attestation / assertion ceremony state in
        // this transient scheme. Per-tenant name so a browser can have a ceremony in flight for several
        // tenants at once.
        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.TwoFactorUserIdScheme)
            .ConfigurePerTenant<CookieAuthenticationOptions, HuiaTenantInfo>((cookie, tenant) =>
                cookie.Cookie.Name = $"{HuiaConstants.Cookies.TwoFactorUser}.{tenant.Identifier}");

        return services;
    }

    /// <summary>
    /// Projects each tenant's passkey policy onto <see cref="IdentityPasskeyOptions"/>: the relying-party
    /// id and allowed origins, the user-verification requirement, the authenticator preference and the
    /// ceremony timeout. The one globally-fixed setting (<c>ResidentKeyRequirement = "required"</c>) is in
    /// <c>AddHuiaIdentity</c>. Mirrors <see cref="AddHuiaPerTenantIdentityOptions"/>:
    /// <see cref="Microsoft.AspNetCore.Identity.PasskeyHandler{TUser}"/> reads <see cref="IOptions{T}"/>, a
    /// process-wide snapshot, so a scoped re-registration re-points it at the tenant-aware value per request.
    /// </summary>
    public static IServiceCollection AddHuiaPerTenantPasskeyOptions(this IServiceCollection services, HuiaOptions options)
    {
        services.AddOptions<IdentityPasskeyOptions>()
            .ConfigurePerTenant<IdentityPasskeyOptions, HuiaTenantInfo>((passkey, tenant) =>
            {
                if (!options.Tenants.TryGetValue(tenant.Identifier, out var config) || config.Authentication.Passkey is not { } policy)
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
    /// default <see cref="IdentityOptions"/> for that tenant — this is what <c>/manage</c>'s
    /// change-password endpoint and the admin API validate against. Must run <em>after</em>
    /// <c>AddHuiaIdentity</c>, which registers the fixed baseline. Neither lockout nor the confirmed-email
    /// / confirmed-phone rules are set here: lockout is per flow now (each flow's own options carry it —
    /// see <c>AddHuiaFlowIdentity</c>), and this default instance is never consulted by a sign-in check
    /// (nothing on the <c>Default</c> flow calls <c>CheckPasswordSignInAsync</c> / <c>IsLockedOutAsync</c>),
    /// so a value here would be inert either way.
    /// </summary>
    /// <remarks>
    /// Finbuckle's <c>ConfigurePerTenant</c> re-projects only <see cref="IOptionsSnapshot{T}"/> /
    /// <see cref="IOptionsMonitor{T}"/>, but <see cref="UserManager{TUser}"/> and
    /// <see cref="SignInManager{TUser}"/> read <see cref="IOptions{T}"/> — a process-wide singleton
    /// snapshot. Both managers are scoped, so the second registration re-points
    /// <c>IOptions&lt;IdentityOptions&gt;</c> at the tenant-aware snapshot for the duration of a request.
    /// </remarks>
    public static IServiceCollection AddHuiaPerTenantIdentityOptions(this IServiceCollection services, HuiaOptions options)
    {
        services.AddOptions<IdentityOptions>()
            .ConfigurePerTenant<IdentityOptions, HuiaTenantInfo>((identity, tenant) =>
            {
                if (!options.Tenants.TryGetValue(tenant.Identifier, out var config))
                {
                    return;
                }

                var emailAndPassword = config.Authentication.EmailAndPassword;
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
