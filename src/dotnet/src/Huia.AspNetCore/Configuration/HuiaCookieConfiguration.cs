using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Huia.AspNetCore.Configuration;

/// <summary>
/// Hardens every cookie Huia issues. Always on (independent of the security-headers opt-in), but the
/// <c>Secure</c> attribute is gated on the transport-security setting so dev/HTTP and in-process test
/// hosts still work.
/// </summary>
internal static class HuiaCookieConfiguration
{
    public static IServiceCollection AddHuiaCookieHardening(this IServiceCollection services, bool requireSecure)
    {
        var securePolicy = requireSecure ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;

        // huia.auth — the interactive session cookie. Deliberately SameSite=Lax: a Strict session cookie
        // is dropped on the RP -> IdP top-level navigation to /connect/authorize, forcing a re-login on
        // every SSO hand-off.
        services.Configure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, options =>
        {
            options.Cookie.Name = HuiaConstants.Cookies.Authentication;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = securePolicy;
            options.SlidingExpiration = true;
        });

        // huia.2fa-user — ASP.NET Core Identity's TwoFactorUserId scheme, reused by its passkey helpers to
        // hold the attestation / assertion ceremony state. SameSite=Lax so it survives the top-level nav
        // from /connect/authorize to the login page; short-lived so a half-finished ceremony cannot linger.
        services.Configure<CookieAuthenticationOptions>(IdentityConstants.TwoFactorUserIdScheme, options =>
        {
            options.Cookie.Name = HuiaConstants.Cookies.TwoFactorUser;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = securePolicy;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
        });

        // huia.csrf — antiforgery. Strict is fine here: antiforgery only matters on same-site form posts.
        services.AddAntiforgery(options =>
        {
            options.Cookie.Name = HuiaConstants.Cookies.AntiForgery;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = securePolicy;
            options.HeaderName = "X-Huia-CSRF";
        });

        services.AddSingleton<IPostConfigureOptions<CookieAuthenticationOptions>, HuiaExternalCookiePostConfigure>();
        return services;
    }

    /// <summary>
    /// Forces the Identity external correlation cookie to <c>SameSite=None</c> + <c>Secure</c> so it
    /// survives the cross-site callback from an external identity provider.
    /// </summary>
    private sealed class HuiaExternalCookiePostConfigure : IPostConfigureOptions<CookieAuthenticationOptions>
    {
        public void PostConfigure(string? name, CookieAuthenticationOptions options)
        {
            if (name == IdentityConstants.ExternalScheme)
            {
                options.Cookie.Name = "huia.external";
                options.Cookie.SameSite = SameSiteMode.None;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            }
        }
    }
}
