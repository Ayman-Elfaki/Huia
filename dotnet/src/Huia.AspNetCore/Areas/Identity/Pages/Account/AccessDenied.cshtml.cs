using Huia.AspNetCore.UI;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Localization;

namespace Huia.AspNetCore.Areas.Identity.Pages.Account;

/// <summary>Shown when an authenticated user lacks permission for a resource.</summary>
public sealed class AccessDeniedModel(IStringLocalizer<SharedResource> localizer) : HuiaAccountPageModel
{
    /// <summary>Handles the GET.</summary>
    public void OnGet()
    {
        ViewData["Title"] = localizer["AccessDenied.Title"].Value;
        ViewData["Heading"] = localizer["AccessDenied.Heading"].Value;
        Response.StatusCode = StatusCodes.Status403Forbidden;
    }
}
