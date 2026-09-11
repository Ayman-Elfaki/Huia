using Huia.Configuration;
using Huia.DependencyInjection;
using Huia.Localization;
using Huia.Options;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Hybrid;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Entry point for registering the Huia identity provider with the DI container. Registers only what
/// every Huia flavor needs regardless of auth mode: the validated options tree, cookie hardening,
/// localization, the in-process event pipeline and generic ASP.NET Core infrastructure (data protection,
/// caching, routing). A host also needs <c>.AddEntityFrameworkCoreStores&lt;...&gt;()</c> (from
/// <c>Huia.EntityFrameworkCore</c>) and a flavor-specific extension — <c>.AddHuiaOpenId()</c> from
/// <c>Huia.OpenId</c>, or <c>.AddHuiaHeadless()</c> from <c>Huia.Headless</c>.
/// </summary>
public static class HuiaServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Huia identity provider. Chain <c>.AddEntityFrameworkCoreStores&lt;...&gt;()</c> and a
    /// flavor extension (<c>.AddHuiaOpenId()</c> / <c>.AddHuiaHeadless()</c>) afterward to complete setup.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the options tree; validated before anything is registered.</param>
    /// <returns>An <see cref="IHuiaBuilder"/> for feature opt-ins.</returns>
    /// <exception cref="HuiaOptionsException">The configured options are invalid.</exception>
    public static IHuiaBuilder AddHuia(this IServiceCollection services, Action<HuiaOptionsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var optionsBuilder = new HuiaOptionsBuilder();
        configure(optionsBuilder);
        var options = optionsBuilder.Build();

        services.AddSingleton(options);
        services.AddSingleton(Options.Options.Create(options));

        services.AddHuiaCookieHardening(requireSecure: !options.DisableTransportSecurityRequirement);
        services.AddHuiaLocalization();
        services.AddHuiaEventing();

        services.AddDataProtection();
        services.AddMemoryCache();
        services.AddHybridCache();
        services.AddRouting();
        services.Configure<RouteOptions>(routing =>
        {
            routing.LowercaseUrls = true;
            routing.LowercaseQueryStrings = false;
        });

        return new HuiaBuilder(services, options);
    }
}
