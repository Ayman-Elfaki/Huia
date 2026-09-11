using Finbuckle.MultiTenant.AspNetCore.Extensions;
using Huia.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.Builder;

/// <summary>Middleware wiring for the Huia OpenID Connect identity provider.</summary>
public static class HuiaOpenIdApplicationBuilderExtensions
{
    /// <summary>
    /// Inserts the Huia OpenId middleware in the one order that works:
    /// exception handling &#8594; status-code pages &#8594; request localization &#8594;
    /// multi-tenant resolution &#8594; (security headers) &#8594; static files &#8594; routing &#8594; authentication &#8594; authorization.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The same application builder, for chaining.</returns>
    public static IApplicationBuilder UseHuiaOpenId(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            ExceptionHandlingPath = "/identity/account/error",
            AllowStatusCode404Response = true,
        });

        // Branded pages for bare status codes (404 / 401 / 403 / ...). The Status page hands API and
        // protocol callers the plain code with no HTML.
        app.UseStatusCodePagesWithReExecute("/identity/account/status/{0}");

        app.UseRequestLocalization();
        app.UseMultiTenant();

        if (app.ApplicationServices.GetService<HuiaSecurityHeadersMarker>() is not null)
        {
            app.UseMiddleware<HuiaSecurityHeadersMiddleware>();
        }

        app.UseStaticFiles();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();

        return app;
    }
}
