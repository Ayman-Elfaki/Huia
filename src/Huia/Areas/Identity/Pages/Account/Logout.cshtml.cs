using Huia.Common;
using Huia.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;

namespace Huia.Areas.Identity.Pages.Account;

/// <summary>Signs the current user out.</summary>
public class LogoutModel(
    HuiaSignInManager signInManager,
    IOpenIddictApplicationManager applicationManager,
    ILogger<LogoutModel> logger) : PageModel
{
    /// <summary>Clears the sign-in cookie and redirects to <paramref name="returnUrl"/> or the login page.</summary>
    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        await signInManager.SignOutAsync();
        logger.LogInformation("User signed out.");

        return returnUrl is not null
            ? Redirect(await ReturnUrlValidator.ResolveAsync(Request, returnUrl, applicationManager))
            : RedirectToPage("./Login");
    }
}
