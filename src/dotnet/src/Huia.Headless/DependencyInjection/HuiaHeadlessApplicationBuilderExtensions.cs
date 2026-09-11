using Finbuckle.MultiTenant.AspNetCore.Extensions;
using Huia.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.Builder;

/// <summary>Middleware wiring for the Huia Headless identity flavor.</summary>
public static class HuiaHeadlessApplicationBuilderExtensions
{
    /// <summary>
    /// Inserts the Huia Headless middleware:
    /// request localization &#8594; multi-tenant resolution &#8594; (security headers) &#8594; routing &#8594; CORS &#8594; authentication &#8594; authorization.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The same application builder, for chaining.</returns>
    public static IApplicationBuilder UseHuiaHeadless(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseRequestLocalization();
        app.UseMultiTenant();

        if (app.ApplicationServices.GetService<HuiaSecurityHeadersMarker>() is not null)
        {
            app.UseMiddleware<HuiaSecurityHeadersMiddleware>();
        }

        app.UseRouting();
        app.UseCors("HuiaHeadlessCors");
        app.UseAuthentication();
        app.UseAuthorization();

        return app;
    }
}
