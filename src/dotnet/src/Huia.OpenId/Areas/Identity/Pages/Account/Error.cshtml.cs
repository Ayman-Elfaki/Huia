using System.Diagnostics;
using Huia.OpenId.Flows;
using Huia.OpenId.UI;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace Huia.OpenId.Areas.Identity.Pages.Account;

/// <summary>Branded error page. Shows a request reference but never exception details.</summary>
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class ErrorModel(IStringLocalizer<SharedResource> localizer) : HuiaAccountPageModel
{
    /// <summary>A correlation id the user can quote to support.</summary>
    public string RequestId { get; private set; } = string.Empty;

    /// <summary>The tenant's client application home, when one is registered. The only link offered here —
    /// the sign-in page is not, since whatever OAuth flow led here is no longer valid.</summary>
    public string? ClientHomeUrl { get; private set; }

    /// <summary>Handles the GET (also reached by the exception handler re-execute).</summary>
    public void OnGet()
    {
        ViewData["Title"] = localizer["Error.Title"].Value;
        ViewData["Heading"] = localizer["Error.Title"].Value;

        ClientHomeUrl = TenantClientHome.Resolve(Tenant);
        RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

        if (HttpContext.Features.Get<IExceptionHandlerFeature>() is not null)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        }
    }
}
