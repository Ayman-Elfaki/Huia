using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Huia.TodoApi.Common;

/// <summary>
/// Adds a bearer-token security scheme to the OpenAPI document so Scalar's UI can send a token.
/// </summary>
internal sealed class BearerSecuritySchemeTransformer(IAuthenticationSchemeProvider authenticationSchemeProvider)
    : IOpenApiDocumentTransformer
{
    public async Task TransformAsync(OpenApiDocument doc, OpenApiDocumentTransformerContext ctx, CancellationToken ct)
    {
        var schemes = await authenticationSchemeProvider.GetAllSchemesAsync();
        if (schemes.All(scheme => scheme.Name != "Bearer")) return;

        var components = doc.Components ?? new OpenApiComponents();
        components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            In = ParameterLocation.Header,
            BearerFormat = "JWT",
        };
        
        doc.Components = components;

        var bearerReference = new OpenApiSecuritySchemeReference("Bearer", doc);

        var operations = doc.Paths.Values
            .Where(path => path.Operations is not null)
            .SelectMany(path => path.Operations!.Values);

        foreach (var operation in operations)
        {
            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement { [bearerReference] = [] });
        }
    }
}