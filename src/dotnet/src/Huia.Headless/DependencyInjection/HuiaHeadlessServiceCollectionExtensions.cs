using Huia.DependencyInjection;
using Huia.Entities;
using Huia.Headless.Multitenancy;
using Huia.Identity;
using Huia.Multitenancy;
using Microsoft.AspNetCore.Identity;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers the single-tenant, bearer-token flavor of Huia: ASP.NET Core Identity's own API endpoints
/// (<c>AddApiEndpoints</c> / <c>MapIdentityApi</c> — register, login, refresh, email confirmation,
/// password reset, 2FA, profile) plus passkeys, on the shared <see cref="HuiaUserManager{TUser}"/> /
/// <see cref="HuiaSignInManager{TUser}"/> / <see cref="HuiaPasskeyRegistrar{TUser}"/> core. No
/// multi-tenancy, no OpenIddict — call <c>.AddHuiaOpenId()</c> from <c>Huia.OpenId</c> instead for that.
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
            .AddUserManager<HuiaUserManager<HuiaUser>>()
            .AddSignInManager<HuiaSignInManager<HuiaUser>>();

        services.AddScoped<HuiaPasskeyRegistrar<HuiaUser>>();

        return builder;
    }
}
