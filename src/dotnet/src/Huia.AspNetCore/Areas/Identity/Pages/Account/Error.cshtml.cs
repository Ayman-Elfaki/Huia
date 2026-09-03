using System.Diagnostics;
using Huia.AspNetCore.UI;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace Huia.AspNetCore.Areas.Identity.Pages.Account;

/// <summary>Branded error page. Shows a request reference but never exception details.</summary>
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class ErrorModel(IStringLocalizer<SharedResource> localizer) : HuiaAccountPageModel
{
    /// <summary>A correlation id the user can quote to support.</summary>
    public string RequestId { get; private set; } = string.Empty;

    /// <summary>Handles the GET (also reached by the exception handler re-execute).</summary>
    public void OnGet()
    {
        ViewData["Title"] = localizer["Error.Title"].Value;
        ViewData["Heading"] = localizer["Error.Title"].Value;

        RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

        if (HttpContext.Features.Get<IExceptionHandlerFeature>() is not null)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        }
    }
}
