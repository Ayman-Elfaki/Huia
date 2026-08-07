using System.Text.Encodings.Web;
using System.Text.Unicode;
using Huia.Applications;
using Huia.Emails;
using Huia.Eventing;
using Huia.Identity;
using Huia.Scopes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using IdentityErrorDescriber = Microsoft.AspNetCore.Identity.IdentityErrorDescriber;

namespace Huia.Core;

/// <summary>
/// Registers Huia's ASP.NET Core Identity, OpenIddict, and key-management services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers ASP.NET Core Identity and an OpenIddict authorization server + local token validation for
    /// the current app. <paramref name="issuer"/> is the issuer URI OpenIddict advertises in tokens and
    /// discovery metadata — it must match where the app is actually served. Chain
    /// <c>.WithEntityFrameworkStores&lt;TContext&gt;()</c> (from <c>Huia.EntityFrameworkCore</c>) onto the
    /// returned builder to wire up persistence, and use <c>huia.KeysManagement.UseAutomaticKeyManagement(...)</c>
    /// inside <paramref name="configure"/> to enable automatic signing/encryption key rotation.
    /// </summary>
    // Long because it's a single flat sequence of independent service registrations (Identity, OpenIddict
    // server/validation, hosted initializers) - splitting it would just relocate, not reduce, that sequence.
#pragma warning disable MA0051
    public static HuiaBuilder AddHuia(this IServiceCollection services, string issuer,
        Action<HuiaOptions> configure)
    {
        var options = new HuiaOptions(issuer, services);
        configure(options);

        services.AddSingleton(options);
        services.AddSingleton(options.Applications);
        services.AddSingleton(options.Scopes);
        services.AddSingleton(options.Branding);
        services.TryAddSingleton<IEmailSender<HuiaUser>, NoOpEmailSender>();
        services.TryAddSingleton<RazorViewRenderer>();
        services.TryAddSingleton<HuiaEmailTemplate>();
        services.TryAddScoped<IEventPublisher, EventPublisher>();

        // ASP.NET Core Identity (via AddIdentityCore below) registers Data Protection with its defaults if
        // nothing else has configured it, which isolates key rings using IHostEnvironment.ContentRootPath as
        // the discriminator unless overridden. That ties every signing key EFCoreSigningKeyStore encrypts
        // (see Huia.EntityFrameworkCore) to the app's physical path — moving or renaming the project directory
        // silently invalidates every previously-stored key, which HuiaKeyManager.RotateAsync surfaces as an
        // unhandled CryptographicException at startup. Pinning a stable, path-independent application name
        // avoids that entirely.
        services.AddDataProtection().SetApplicationName("Huia");

        // Huia.UI's pages (Areas/Identity/Pages) and error page are Razor Pages; consumers still need to
        // call app.MapRazorPages() themselves, but registering the services here means they don't also need
        // to remember services.AddRazorPages().
        services.AddRazorPages();

        // Razor's default HtmlEncoder only allows Basic Latin through as-is; anything else (Arabic and every
        // other non-Latin culture Huia.Localization might be configured for) gets rewritten as numeric HTML
        // character references (e.g. "&#x62A;") instead of the literal UTF-8 text. That's still valid,
        // correctly-rendering HTML — but every page and email declares charset=utf-8 already (see _Layout.cshtml,
        // EmailBody.cshtml), making the escaping pure overhead, and it turns otherwise-simple "does this
        // contain the Arabic string" checks (tests, log inspection, view-source) into needing to decode
        // entities first. Allowing the full Unicode range removes that overhead for every culture, present or
        // future, rather than special-casing just Arabic. AddRazorPages() above already registers a default
        // HtmlEncoder via TryAddSingleton; this must be a plain AddSingleton (last registration wins) to
        // actually override it rather than losing to whichever one registered first.
        services.AddSingleton(HtmlEncoder.Create(UnicodeRanges.All));

        services.AddLocalization();
        services.Configure<RequestLocalizationOptions>(localization =>
        {
            var cultures = options.Localization.Cultures;
            localization.SupportedCultures = [.. cultures];
            localization.SupportedUICultures = [.. cultures];
            localization.SetDefaultCulture(options.Localization.DefaultCulture);
        });

        // Huia's Razor Pages route by PascalCase file/folder name (e.g. "/Identity/Account/Login") by
        // default; this makes every URL generated through routing (Url.Page, asp-page, RedirectToPage) come
        // out lowercase instead, matching the connect/manage endpoints' own hardcoded lowercase paths.
        services.Configure<RouteOptions>(route => route.LowercaseUrls = true);

        services.AddIdentityCore<HuiaUser>(identity =>
            {
                identity.SignIn.RequireConfirmedAccount = false;
                options.Identity = identity;
            })
            .AddRoles<HuiaRole>()
            .AddSignInManager()
            .AddDefaultTokenProviders()
            .AddErrorDescriber<IdentityErrorDescriber>();

        // The standard Identity.Application cookie scheme, not a custom one: SignInManager<HuiaUser>'s
        // high-level methods (PasswordSignInAsync, TwoFactorSignInAsync, lockout handling, etc. — all used
        // by Huia's Razor Pages) target IdentityConstants.ApplicationScheme by convention.
        services.AddAuthentication(auth =>
            {
                auth.DefaultScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
                auth.DefaultSignInScheme = IdentityConstants.ApplicationScheme;
            })
            .AddIdentityCookies();

        services.ConfigureApplicationCookie(cookie =>
        {
            cookie.ExpireTimeSpan = TimeSpan.FromMinutes(10);
            cookie.SlidingExpiration = false;
            cookie.LoginPath = options.LoginPath;

            // Prevent 302 redirect for APIs on unauthorized (forbidden) requests
            cookie.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
            
            cookie.Events.OnRedirectToLogin = context =>
            {
                // Huia's own JSON endpoints (manage/*, device verification) should 401, not redirect to an
                // HTML page; /connect/authorize handles its own redirect-to-login explicitly instead of
                // relying on this challenge path.
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
        });

        services.AddAuthorization();

        services.AddOpenIddict()
            .AddServer(server =>
            {
                server.SetIssuer(new Uri(options.Issuer));

                server.SetAuthorizationEndpointUris("connect/authorize")
                    .SetTokenEndpointUris("connect/token")
                    .SetUserInfoEndpointUris("connect/userinfo")
                    .SetEndSessionEndpointUris("connect/logout")
                    .SetDeviceAuthorizationEndpointUris("connect/device")
                    .SetEndUserVerificationEndpointUris("identity/account/device", "connect/verify");

                server.AllowAuthorizationCodeFlow()
                    .AllowRefreshTokenFlow()
                    .AllowClientCredentialsFlow()
                    .AllowDeviceAuthorizationFlow();

                server.AddDevelopmentEncryptionCertificate()
                    .AddDevelopmentSigningCertificate();

                string[] wellKnownScopes =
                [
                    OpenIddictConstants.Scopes.Roles,
                    OpenIddictConstants.Scopes.Email,
                    OpenIddictConstants.Scopes.OpenId,
                    OpenIddictConstants.Scopes.Profile,
                    OpenIddictConstants.Scopes.OfflineAccess
                ];

                var declaredScopes = options.Scopes.Scopes.Select(s => s.Name);
                server.RegisterScopes([.. wellKnownScopes, .. options.Applications.GetAllScopes(), .. declaredScopes]);

                var aspNetCore = server.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableUserInfoEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough()
                    .EnableEndUserVerificationEndpointPassthrough()
                    .EnableStatusCodePagesIntegration();

                if (options.InsecureHttpAllowed)
                {
                    aspNetCore.DisableTransportSecurityRequirement();
                }

                if (options.AccessTokenEncryptionDisabled)
                {
                    server.DisableAccessTokenEncryption();
                }
            })
            .AddValidation(validation =>
            {
                validation.UseLocalServer();
                validation.UseAspNetCore();

                if (options.RequiredAudiences.Count > 0)
                {
                    validation.AddAudiences([.. options.RequiredAudiences]);
                }
            });

        services.AddHostedService<ApplicationInitializer>();
        services.AddHostedService<ScopeInitializer>();

        return new HuiaBuilder(services);
    }
#pragma warning restore MA0051
}