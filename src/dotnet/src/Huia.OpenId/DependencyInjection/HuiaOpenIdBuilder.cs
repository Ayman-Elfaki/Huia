using Finbuckle.MultiTenant;
using Huia.DependencyInjection;
using Huia.OpenId.EntityFrameworkCore.Entities;
using Huia.OpenId.EntityFrameworkCore.Multitenancy;
using Huia.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.OpenId.DependencyInjection;

/// <summary>
/// Implementation of <see cref="IHuiaOpenIdBuilder"/>.
/// </summary>
internal sealed class HuiaOpenIdBuilder(IServiceCollection services, HuiaOptions options) : IHuiaOpenIdBuilder
{
    public IServiceCollection Services { get; } = services;

    public HuiaOptions Options { get; } = options;

    public IHuiaOpenIdBuilder ConfigureOpenIddict(Action<global::Microsoft.Extensions.DependencyInjection.OpenIddictBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = Services.AddOpenIddict();
        configure(builder);
        return this;
    }

    public IHuiaOpenIdBuilder ConfigureOpenIddictServer(Action<global::Microsoft.Extensions.DependencyInjection.OpenIddictServerBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        Services.AddOpenIddict().AddServer(configure);
        return this;
    }

    public IHuiaOpenIdBuilder ConfigureOpenIddictValidation(Action<global::Microsoft.Extensions.DependencyInjection.OpenIddictValidationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        Services.AddOpenIddict().AddValidation(configure);
        return this;
    }

    public IHuiaOpenIdBuilder ConfigureOpenIddictClient(Action<global::Microsoft.Extensions.DependencyInjection.OpenIddictClientBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        Services.AddOpenIddict().AddClient(configure);
        return this;
    }

    public IHuiaOpenIdBuilder ConfigureOpenIddictCore(Action<global::Microsoft.Extensions.DependencyInjection.OpenIddictCoreBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        Services.AddOpenIddict().AddCore(configure);
        return this;
    }

    public IHuiaOpenIdBuilder ConfigureFinbuckle(Action<MultiTenantBuilder<HuiaTenantInfo>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new MultiTenantBuilder<HuiaTenantInfo>(Services);
        configure(builder);
        return this;
    }

    public IHuiaOpenIdBuilder ConfigureIdentity(Action<IdentityBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new IdentityBuilder(typeof(HuiaUser), typeof(HuiaRole), Services);
        configure(builder);
        return this;
    }

    public IHuiaOpenIdBuilder ConfigureAuthentication(Action<AuthenticationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new AuthenticationBuilder(Services);
        configure(builder);
        return this;
    }
}
