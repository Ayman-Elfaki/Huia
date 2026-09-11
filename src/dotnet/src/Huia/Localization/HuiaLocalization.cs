using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.Localization;

/// <summary>Localization support for the account UI and emails. English and Arabic (right-to-left).</summary>
public static class HuiaLocalization
{
    /// <summary>The cultures Huia ships translations for. The first entry is the default.</summary>
    public static readonly IReadOnlyList<string> SupportedCultures = ["en", "ar"];

    internal static IServiceCollection AddHuiaLocalization(this IServiceCollection services)
    {
        services.AddLocalization(options => options.ResourcesPath = "Resources");

        services.Configure<RequestLocalizationOptions>(options =>
        {
            var cultures = SupportedCultures.Select(c => new CultureInfo(c)).ToArray();
            options.DefaultRequestCulture = new RequestCulture(cultures[0]);
            options.SupportedCultures = cultures;
            options.SupportedUICultures = cultures;
            options.ApplyCurrentCultureToResponseHeaders = true;

            // Order: explicit ?culture= wins, then the OIDC ui_locales authorize hint, then the
            // persisted cookie, then Accept-Language.
            options.RequestCultureProviders =
            [
                new QueryStringRequestCultureProvider(),
                new UiLocalesRequestCultureProvider(),
                new CookieRequestCultureProvider(),
                new AcceptLanguageHeaderRequestCultureProvider(),
            ];
        });

        return services;
    }

    /// <summary>Whether a culture is written right-to-left (drives <c>dir="rtl"</c> on the account UI).</summary>
    /// <param name="culture">The culture to test.</param>
    /// <returns><see langword="true"/> for right-to-left cultures such as Arabic.</returns>
    public static bool IsRightToLeft(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return culture.TextInfo.IsRightToLeft;
    }
}
