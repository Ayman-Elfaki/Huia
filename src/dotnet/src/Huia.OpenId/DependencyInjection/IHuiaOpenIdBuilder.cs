using Finbuckle.MultiTenant;
using Huia.DependencyInjection;
using Huia.OpenId.EntityFrameworkCore.Multitenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.OpenId.DependencyInjection;

/// <summary>
/// Builder returned by <c>AddHuiaOpenId</c> allowing feature opt-ins and direct configuration
/// of lower-level identity and multi-tenancy frameworks (OpenIddict, Finbuckle, ASP.NET Core Identity).
/// </summary>
public interface IHuiaOpenIdBuilder : IHuiaBuilder
{
    /// <summary>Configures the root OpenIddict builder.</summary>
    IHuiaOpenIdBuilder ConfigureOpenIddict(Action<global::Microsoft.Extensions.DependencyInjection.OpenIddictBuilder> configure);

    /// <summary>Configures the OpenIddict Server builder.</summary>
    IHuiaOpenIdBuilder ConfigureOpenIddictServer(Action<global::Microsoft.Extensions.DependencyInjection.OpenIddictServerBuilder> configure);

    /// <summary>Configures the OpenIddict Validation builder.</summary>
    IHuiaOpenIdBuilder ConfigureOpenIddictValidation(Action<global::Microsoft.Extensions.DependencyInjection.OpenIddictValidationBuilder> configure);

    /// <summary>Configures the OpenIddict Client builder.</summary>
    IHuiaOpenIdBuilder ConfigureOpenIddictClient(Action<global::Microsoft.Extensions.DependencyInjection.OpenIddictClientBuilder> configure);

    /// <summary>Configures the OpenIddict Core builder.</summary>
    IHuiaOpenIdBuilder ConfigureOpenIddictCore(Action<global::Microsoft.Extensions.DependencyInjection.OpenIddictCoreBuilder> configure);

    /// <summary>Configures the Finbuckle MultiTenant builder.</summary>
    IHuiaOpenIdBuilder ConfigureFinbuckle(Action<MultiTenantBuilder<HuiaTenantInfo>> configure);

    /// <summary>Configures the ASP.NET Core Identity builder.</summary>
    IHuiaOpenIdBuilder ConfigureIdentity(Action<IdentityBuilder> configure);

    /// <summary>Configures the ASP.NET Core Authentication builder.</summary>
    IHuiaOpenIdBuilder ConfigureAuthentication(Action<AuthenticationBuilder> configure);
}
