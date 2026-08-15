namespace Huia.IdentityServer.Common;

public static class WebApplicationBuilderExtensions
{
    // Same approach as Huia.TodoApi's own ResolveIssuer: derives the OpenIddict issuer from the address
    // Aspire actually assigned this process (not known until runtime), falling back to a fixed dev port
    // when run standalone (outside the AppHost).
    public static string ResolveIssuer(this WebApplicationBuilder builder)
    {
        var configured = builder.Configuration["Oidc:Issuer"];
        if (!string.IsNullOrEmpty(configured))
        {
            return configured;
        }

        var urls = builder.Configuration["ASPNETCORE_URLS"] ?? builder.Configuration["urls"];
        var candidates = urls?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

        return candidates.FirstOrDefault(u => u.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
               ?? candidates.FirstOrDefault()
               ?? "https://localhost:5051";
    }
}
