using Huia.AspNetCore.UI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace Huia.AspNetCore.Areas.Identity.Pages.Account;

/// <summary>Told the user to check their inbox after self-service registration.</summary>
public sealed class RegisterConfirmationModel(IStringLocalizer<SharedResource> localizer) : HuiaAccountPageModel
{
    /// <summary>The return URL to carry back to the sign-in link.</summary>
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    /// <summary>Handles the GET.</summary>
    public void OnGet()
    {
        ViewData["Title"] = localizer["RegisterConfirmation.Title"].Value;
        ViewData["Heading"] = localizer["RegisterConfirmation.Heading"].Value;
    }
}
