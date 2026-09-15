using Huia.Configuration;
using Huia.DependencyInjection;
using Huia.Localization;
using Huia.Options;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Hybrid;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Shared plumbing behind both Huia flavors. Registers only what every flavor needs regardless of auth
/// mode: the validated options tree, cookie hardening, localization, the in-process event pipeline and
/// generic ASP.NET Core infrastructure (data protection, caching, routing). Not part of the public API —
/// a host calls <c>.AddHuiaOpenId(...)</c> (from <c>Huia.OpenId</c>) or <c>.AddHuiaHeadless(...)</c> (from
/// <c>Huia.Headless</c>) instead, each of which builds its own <see cref="HuiaOptions"/> tree and calls
/// into this internally.
/// </summary>
internal static class HuiaServiceCollectionExtensions
{
    /// <summary>
    /// Registers the shared Huia plumbing against an already-built, already-validated options tree.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="options">The validated options tree, built by the calling flavor's own builder.</param>
    /// <returns>An <see cref="IHuiaBuilder"/> for feature opt-ins.</returns>
    internal static IHuiaBuilder AddHuiaCore(this IServiceCollection services, HuiaOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

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
