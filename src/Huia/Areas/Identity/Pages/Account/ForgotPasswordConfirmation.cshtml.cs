using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Huia.Areas.Identity.Pages.Account;

/// <summary>Shown after a password-reset email is requested (regardless of whether the address exists).</summary>
[AllowAnonymous]
public class ForgotPasswordConfirmationModel : PageModel
{
    /// <summary>Where to send the user once they sign in — carried through from <c>ForgotPassword</c>.</summary>
    public string? ReturnUrl { get; set; }

    /// <summary>Renders the page.</summary>
    public void OnGet(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
    }
}
