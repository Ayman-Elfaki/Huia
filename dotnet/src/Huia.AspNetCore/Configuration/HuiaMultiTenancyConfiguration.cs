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

        return services;
    }

    /// <summary>
    /// Projects each tenant's password-complexity and lockout policy (and the opt-in unique-email rule)
    /// onto <see cref="IdentityOptions"/> for that tenant. Must run <em>after</em> <c>AddHuiaIdentity</c>,
    /// which registers the process-global fallback. The confirmed-email / confirmed-phone rules are left
    /// to that fallback plus the flow-aware <c>HuiaUserConfirmation</c> — mapping them onto
    /// <see cref="SignInOptions"/> here would block password sign-in for phone-less accounts.
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

                var password = config.Authentication.Password;
                identity.Password.RequiredLength = password.MinimumLength;
                identity.Password.RequireDigit = password.RequireDigit;
                identity.Password.RequireLowercase = password.RequireLowercase;
                identity.Password.RequireUppercase = password.RequireUppercase;
                identity.Password.RequireNonAlphanumeric = password.RequireNonAlphanumeric;
                identity.Password.RequiredUniqueChars = password.RequiredUniqueChars;

                identity.Lockout.MaxFailedAccessAttempts = config.Lockout.MaxFailedAccessAttempts;
                identity.Lockout.DefaultLockoutTimeSpan = config.Lockout.LockoutDuration;
                identity.Lockout.AllowedForNewUsers = config.Lockout.AllowedForNewUsers;

                identity.User.RequireUniqueEmail = password.RequireUniqueEmail;
            });

        services.AddScoped<IOptions<IdentityOptions>>(sp =>
            new OptionsWrapper<IdentityOptions>(sp.GetRequiredService<IOptionsSnapshot<IdentityOptions>>().Value));

        return services;
    }
}
