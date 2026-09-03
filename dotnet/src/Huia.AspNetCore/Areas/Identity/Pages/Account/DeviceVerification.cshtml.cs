using Huia.AspNetCore.UI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace Huia.AspNetCore.Areas.Identity.Pages.Account;

/// <summary>
/// The device-flow end-user verification form. The user confirms (or edits) the code shown on their
/// input-constrained device; the form posts to <c>/connect/verify</c>, where OpenIddict associates the
/// decision with the device code.
/// </summary>
[Authorize]
public sealed class DeviceVerificationModel(IStringLocalizer<SharedResource> localizer) : HuiaAccountPageModel
{
    /// <summary>The user code, prefilled from the <c>user_code</c> query string when present.</summary>
    [BindProperty(SupportsGet = true, Name = "user_code")]
    public string? UserCode { get; set; }

    /// <summary>Renders the confirmation form.</summary>
    public void OnGet()
    {
        ViewData["Title"] = localizer["DeviceVerification.Title"].Value;
        ViewData["Heading"] = localizer["DeviceVerification.Heading"].Value;

        if (TempData["DeviceVerificationError"] is string)
        {
            ErrorMessage = localizer["DeviceVerification.Invalid"].Value;
        }
    }
}
