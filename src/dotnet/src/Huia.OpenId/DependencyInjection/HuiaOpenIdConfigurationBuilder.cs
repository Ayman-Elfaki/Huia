using Finbuckle.MultiTenant;
using Huia.OpenId.EntityFrameworkCore.Multitenancy;
using Huia.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.OpenId.DependencyInjection;

/// <summary>
/// Configuration builder for <c>AddHuiaOpenId</c> that combines Huia options with lower-level
/// hooks for OpenIddict, Finbuckle, ASP.NET Core Identity, and Authentication.
/// </summary>
public class HuiaOpenIdConfigurationBuilder : HuiaOptionsBuilder
{
    /// <summary>Lower-level OpenIddict root configuration callbacks.</summary>
    public IList<Action<global::Microsoft.Extensions.DependencyInjection.OpenIddictBuilder>> OpenIddictConfigurations { get; } = [];

    /// <summary>Lower-level OpenIddict Server configuration callbacks.</summary>
    public IList<Action<global::Microsoft.Extensions.DependencyInjection.OpenIddictServerBuilder>> OpenIddictServerConfigurations { get; } = [];

    /// <summary>Lower-level OpenIddict Validation configuration callbacks.</summary>
    public IList<Action<global::Microsoft.Extensions.DependencyInjection.OpenIddictValidationBuilder>> OpenIddictValidationConfigurations { get; } = [];

    /// <summary>Lower-level OpenIddict Client configuration callbacks.</summary>
    public IList<Action<global::Microsoft.Extensions.DependencyInjection.OpenIddictClientBuilder>> OpenIddictClientConfigurations { get; } = [];

    /// <summary>Lower-level OpenIddict Core configuration callbacks.</summary>
    public IList<Action<global::Microsoft.Extensions.DependencyInjection.OpenIddictCoreBuilder>> OpenIddictCoreConfigurations { get; } = [];

    /// <summary>Lower-level Finbuckle MultiTenant configuration callbacks.</summary>
    public IList<Action<MultiTenantBuilder<HuiaTenantInfo>>> FinbuckleConfigurations { get; } = [];

    /// <summary>Lower-level ASP.NET Core Identity configuration callbacks.</summary>
    public IList<Action<IdentityBuilder>> IdentityConfigurations { get; } = [];

    /// <summary>Lower-level ASP.NET Core Authentication configuration callbacks.</summary>
    public IList<Action<AuthenticationBuilder>> AuthenticationConfigurations { get; } = [];

    /// <summary>Configures the root OpenIddict builder.</summary>
    public HuiaOpenIdConfigurationBuilder ConfigureOpenIddict(Action<global::Microsoft.Extensions.DependencyInjection.OpenIddictBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        OpenIddictConfigurations.Add(configure);
        return this;
    }

    /// <summary>Configures the OpenIddict Server builder.</summary>
    public HuiaOpenIdConfigurationBuilder ConfigureOpenIddictServer(Action<global::Microsoft.Extensions.DependencyInjection.OpenIddictServerBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        OpenIddictServerConfigurations.Add(configure);
        return this;
    }

    /// <summary>Configures the OpenIddict Validation builder.</summary>
    public HuiaOpenIdConfigurationBuilder ConfigureOpenIddictValidation(Action<global::Microsoft.Extensions.DependencyInjection.OpenIddictValidationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        OpenIddictValidationConfigurations.Add(configure);
        return this;
    }

    /// <summary>Configures the OpenIddict Client builder.</summary>
    public HuiaOpenIdConfigurationBuilder ConfigureOpenIddictClient(Action<global::Microsoft.Extensions.DependencyInjection.OpenIddictClientBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        OpenIddictClientConfigurations.Add(configure);
        return this;
    }

    /// <summary>Configures the OpenIddict Core builder.</summary>
    public HuiaOpenIdConfigurationBuilder ConfigureOpenIddictCore(Action<global::Microsoft.Extensions.DependencyInjection.OpenIddictCoreBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        OpenIddictCoreConfigurations.Add(configure);
        return this;
    }

    /// <summary>Configures the Finbuckle MultiTenant builder.</summary>
    public HuiaOpenIdConfigurationBuilder ConfigureFinbuckle(Action<MultiTenantBuilder<HuiaTenantInfo>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        FinbuckleConfigurations.Add(configure);
        return this;
    }

    /// <summary>Configures the ASP.NET Core Identity builder.</summary>
    public HuiaOpenIdConfigurationBuilder ConfigureIdentity(Action<IdentityBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        IdentityConfigurations.Add(configure);
        return this;
    }

    /// <summary>Configures the ASP.NET Core Authentication builder.</summary>
    public HuiaOpenIdConfigurationBuilder ConfigureAuthentication(Action<AuthenticationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        AuthenticationConfigurations.Add(configure);
        return this;
    }
}
