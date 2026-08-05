using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.Emails;

/// <summary>
/// Renders a Razor view (by its project-relative path, e.g. <c>/Identity/EmailTemplates/EmailBody.cshtml</c>)
/// to a string outside an HTTP request — used by <see cref="HuiaEmailTemplate"/> to render transactional
/// email bodies with the same Razor/Basecoat conventions Huia.UI's pages use, instead of hand-built HTML.
/// </summary>
public sealed class RazorViewRenderer(
    IRazorViewEngine viewEngine,
    ITempDataProvider tempDataProvider,
    IServiceScopeFactory scopeFactory)
{
    /// <summary>Renders the Razor view at <paramref name="viewPath"/> (e.g. <c>/Identity/EmailTemplates/EmailBody.cshtml</c>) with <paramref name="model"/> and returns the resulting HTML.</summary>
    public async Task<string> RenderAsync<TModel>(string viewPath, TModel model)
    {
        // RazorView.RenderAsync resolves scoped services (e.g. IViewBufferScope) off HttpContext.RequestServices,
        // which must therefore be a scope, not the root provider.
        using var scope = scopeFactory.CreateScope();
        var httpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());

        var viewResult = viewEngine.GetView(executingFilePath: null, viewPath, isMainPage: true);
        if (!viewResult.Success)
        {
            throw new InvalidOperationException(
                $"Razor view '{viewPath}' could not be found. Locations searched: " +
                string.Join(", ", viewResult.SearchedLocations));
        }

        await using var output = new StringWriter();
        var viewData = new ViewDataDictionary<TModel>(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        {
            Model = model
        };
        var viewContext = new ViewContext(
            actionContext,
            viewResult.View,
            viewData,
            new TempDataDictionary(httpContext, tempDataProvider),
            output,
            new HtmlHelperOptions());

        await viewResult.View.RenderAsync(viewContext);
        return output.ToString();
    }
}