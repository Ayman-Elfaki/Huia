using Microsoft.AspNetCore.Builder;

namespace Huia.Core;


/// <summary>
/// Wires Huia's request-pipeline middleware onto an <see cref="IApplicationBuilder"/>.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Renders Huia's own branded error page (<c>Areas/Identity/Pages/Error.cshtml</c>) for unhandled
    /// exceptions and for status-code responses that can't be redirected back to the client — in
    /// particular, OpenIddict's connect endpoints (<c>EnableStatusCodePagesIntegration()</c>) land here for
    /// failures like an unknown <c>client_id</c> or <c>redirect_uri</c>. Also applies the culture requested
    /// via <c>huia.Localization</c> (query string, cookie, or <c>Accept-Language</c>) to the request.
    /// <c>services.AddHuia(...)</c> already calls <c>AddRazorPages()</c>; still requires <c>MapRazorPages()</c>
    /// to be called on the endpoint routes (as with any other Razor Pages app).
    /// </summary>
    /// <remarks>
    /// Call this early in the pipeline, before <c>UseAuthentication</c>/<c>UseAuthorization</c> and the
    /// <c>MapHuia*Endpoints</c> calls — mirroring where ASP.NET Core's own
    /// <c>UseExceptionHandler</c>/<c>UseStatusCodePages</c> are conventionally placed. In Development, add
    /// the framework's developer exception page <em>before</em> this call so it keeps handling unhandled
    /// exceptions (it shows the actual stack trace) — exception-handling middleware registered earlier wins,
    /// so this doesn't shadow it:
    /// <code>
    /// if (app.Environment.IsDevelopment())
    /// {
    ///     app.UseDeveloperExceptionPage();
    /// }
    ///
    /// app.UseHuia();
    /// </code>
    /// Status-code handling (OpenIddict's connect-endpoint failures in particular) is branded in every
    /// environment either way.
    /// </remarks>
    public static IApplicationBuilder UseHuia(this IApplicationBuilder app)
    {
        app.UseRequestLocalization();
        app.UseExceptionHandler("/identity/error");
        app.UseStatusCodePagesWithReExecute("/identity/error/{0}");

        return app;
    }
}
