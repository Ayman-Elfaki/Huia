using Finbuckle.MultiTenant;
using Finbuckle.MultiTenant.AspNetCore.Extensions;
using Finbuckle.MultiTenant.Extensions;
using Huia.Multitenancy;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.OpenId.Configuration;

internal static class HuiaOpenIdMultiTenancyConfiguration
{
    public static IServiceCollection AddHuiaPerTenantAuthentication(this IServiceCollection services)
    {
        new MultiTenantBuilder<HuiaTenantInfo>(services).WithPerTenantAuthentication();

        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .ConfigurePerTenant<CookieAuthenticationOptions, HuiaTenantInfo>((cookie, tenant) =>
                cookie.Cookie.Name = $"{HuiaOpenIdConstants.Cookies.Authentication}.{tenant.Identifier}");

        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.TwoFactorRememberMeScheme)
            .ConfigurePerTenant<CookieAuthenticationOptions, HuiaTenantInfo>((cookie, tenant) =>
                cookie.Cookie.Name = $"{HuiaOpenIdConstants.Cookies.TwoFactorUser}.{tenant.Identifier}");

        return services;
    }
}
