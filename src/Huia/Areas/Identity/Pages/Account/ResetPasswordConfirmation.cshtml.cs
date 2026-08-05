using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Huia.Areas.Identity.Pages.Account;

/// <summary>Shown after a password reset attempt, whether or not the account existed.</summary>
[AllowAnonymous]
public class ResetPasswordConfirmationModel : PageModel
{
    /// <summary>Where to send the user once they sign in — carried through from <c>ResetPassword</c>.</summary>
    public string? ReturnUrl { get; set; }

    /// <summary>Renders the page.</summary>
    public void OnGet(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
    }
}
