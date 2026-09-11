using Finbuckle.MultiTenant.AspNetCore.Extensions;
using Huia.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.Builder;

/// <summary>Middleware wiring for common Huia services.</summary>
public static class HuiaApplicationBuilderExtensions
{
    /// <summary>
    /// Inserts the common Huia middleware in order:
    /// request localization -> multi-tenant resolution -> (security headers) -> routing -> authentication -> authorization.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The same application builder, for chaining.</returns>
    public static IApplicationBuilder UseHuia(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseRequestLocalization();
        app.UseMultiTenant();

        if (app.ApplicationServices.GetService<HuiaSecurityHeadersMarker>() is not null)
        {
            app.UseMiddleware<HuiaSecurityHeadersMiddleware>();
        }

        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();

        return app;
    }
}
