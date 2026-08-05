using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Huia.Areas.Identity.Pages.Account;

/// <summary>Shown after registration when email confirmation is required before signing in.</summary>
[AllowAnonymous]
public class RegisterConfirmationModel : PageModel
{
    /// <summary>The address the confirmation email was sent to.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Where to send the user once they sign in — carried through from <c>Register</c>.</summary>
    public string? ReturnUrl { get; set; }

    /// <summary>Renders the page for <paramref name="email"/>.</summary>
    public void OnGet(string email, string? returnUrl = null)
    {
        Email = email;
        ReturnUrl = returnUrl;
    }
}
