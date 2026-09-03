using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;

namespace Huia.AspNetCore.Emails;

/// <summary>
/// Renders a non-page <c>.cshtml</c> email template to an HTML string. Resolves the compiled RCL view by
/// app-relative path so it works both inside a request and from a background job.
/// </summary>
public sealed class RazorEmailRenderer(
    IRazorViewEngine viewEngine,
    ITempDataProvider tempDataProvider,
    IServiceProvider services,
    IHttpContextAccessor httpContextAccessor)
{
    /// <summary>Renders the template at <paramref name="viewPath"/> with <paramref name="model"/>.</summary>
    /// <typeparam name="TModel">The model type.</typeparam>
    /// <param name="viewPath">App-relative path, e.g. <c>/Emails/Views/ConfirmEmail.cshtml</c>.</param>
    /// <param name="model">The view model.</param>
    /// <returns>The rendered HTML.</returns>
    public async Task<string> RenderAsync<TModel>(string viewPath, TModel model)
    {
        ArgumentException.ThrowIfNullOrEmpty(viewPath);

        var httpContext = httpContextAccessor.HttpContext ?? new DefaultHttpContext { RequestServices = services };
        var actionContext = new ActionContext(httpContext, httpContext.GetRouteData() ?? new RouteData(), new ActionDescriptor());

        var view = viewEngine.GetView(executingFilePath: null, viewPath, isMainPage: true);
        if (!view.Success)
        {
            view = viewEngine.FindView(actionContext, viewPath, isMainPage: true);
        }

        if (view.View is null)
        {
            throw new InvalidOperationException(
                $"Email template '{viewPath}' was not found. Searched: {string.Join(", ", view.SearchedLocations ?? [])}");
        }

        await using var writer = new StringWriter();
        var viewData = new ViewDataDictionary<TModel>(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        {
            Model = model,
        };

        var viewContext = new ViewContext(
            actionContext,
            view.View,
            viewData,
            new TempDataDictionary(httpContext, tempDataProvider),
            writer,
            new HtmlHelperOptions());

        await view.View.RenderAsync(viewContext);
        return writer.ToString();
    }
}
