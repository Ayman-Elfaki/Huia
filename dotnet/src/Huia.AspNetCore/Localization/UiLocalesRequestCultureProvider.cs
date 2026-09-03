using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;

namespace Huia.AspNetCore.Localization;

/// <summary>
/// Resolves the request culture from the OpenID Connect <c>ui_locales</c> authorize parameter: a
/// space-delimited, most-preferred-first list of BCP-47 language tags. The first tag Huia ships a
/// translation for wins. The resolved culture is also written to the standard localization cookie so it
/// survives the redirect from <c>/connect/authorize</c> to the account UI pages.
/// </summary>
public sealed class UiLocalesRequestCultureProvider : RequestCultureProvider
{
    /// <summary>The authorize-request query parameter this provider reads.</summary>
    public const string ParameterName = "ui_locales";

    /// <inheritdoc />
    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (!httpContext.Request.Query.TryGetValue(ParameterName, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return NullProviderCultureResult;
        }

        // Prefer the middleware-supplied supported set; fall back to what Huia ships when it is not wired
        // (RequestLocalizationMiddleware does not always populate Options on custom providers).
        var supported = Options?.SupportedUICultures is { Count: > 0 } fromOptions
            ? fromOptions.Select(c => c.Name)
            : HuiaLocalization.SupportedCultures;

        var supportedList = supported as IReadOnlyList<string> ?? supported.ToArray();
        var tags = raw.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var tag in tags)
        {
            if (Match(tag, supportedList) is not { } culture)
            {
                continue;
            }

            PersistCultureCookie(httpContext, culture);
            return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(culture, culture));
        }

        return NullProviderCultureResult;
    }

    private static string? Match(string tag, IReadOnlyList<string> supported)
    {
        foreach (var name in supported)
        {
            if (string.Equals(name, tag, StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }

        // "ar-SA" -> "ar": fall back to the primary language subtag.
        var primary = tag.Split('-', 2)[0];
        foreach (var name in supported)
        {
            if (string.Equals(name, primary, StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }

        return null;
    }

    private static void PersistCultureCookie(HttpContext httpContext, string culture)
    {
        httpContext.Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
            new CookieOptions
            {
                Path = "/",
                IsEssential = true,
                HttpOnly = false,
                SameSite = SameSiteMode.Lax,
                Secure = httpContext.Request.IsHttps,
                MaxAge = TimeSpan.FromDays(30),
            });
    }
}
