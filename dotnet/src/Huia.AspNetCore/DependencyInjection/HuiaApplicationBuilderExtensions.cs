using Finbuckle.MultiTenant.AspNetCore.Extensions;
using Huia.AspNetCore.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.Builder;

/// <summary>Middleware wiring for the Huia identity provider.</summary>
public static class HuiaApplicationBuilderExtensions
{
    /// <summary>
    /// Inserts the Huia middleware in the one order that works:
    /// exception handling &#8594; status-code pages &#8594; request localization &#8594;
    /// multi-tenant resolution &#8594; (security headers) &#8594; routing &#8594; authentication &#8594; authorization.
    /// </summary>
    /// <remarks>
    /// <c>UseMultiTenant()</c> must run before <c>UseRouting()</c>: with the base-path strategy and
    /// <c>RebaseAspNetCorePathBase</c>, routing would otherwise try to match <c>/{tenant}/connect/token</c>
    /// before the tenant segment is moved into <c>PathBase</c>, and every endpoint would 404.
    /// </remarks>
    /// <param name="app">The application builder.</param>
    /// <returns>The same application builder, for chaining.</returns>
    public static IApplicationBuilder UseHuia(this IApplicationBuilder app)
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
