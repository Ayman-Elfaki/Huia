using System.Text;
using Huia.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace Huia.Areas.Identity.Pages.Account;

/// <summary>Confirms a user's email address from the link sent during registration.</summary>
[AllowAnonymous]
public class ConfirmEmailModel(UserManager<HuiaUser> userManager) : PageModel
{
    /// <summary>Whether the confirmation succeeded.</summary>
    public bool Succeeded { get; private set; }

    /// <summary>
    /// Where to send the user once they sign in — carried through from the link <c>Register</c> emailed them,
    /// so an in-progress OAuth flow isn't lost by the round trip through the user's inbox.
    /// </summary>
    public string? ReturnUrl { get; private set; }

    /// <summary>Confirms the email for <paramref name="userId"/> using the token in <paramref name="code"/>.</summary>
    public async Task<IActionResult> OnGetAsync(string? userId, string? code, string? returnUrl = null)
    {
        ReturnUrl = returnUrl;

        if (userId is null || code is null)
        {
            return RedirectToPage("./Login", new { returnUrl });
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            Succeeded = false;
            return Page();
        }

        string decodedCode;
        try
        {
            decodedCode = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
        }
        catch (FormatException)
        {
            Succeeded = false;
            return Page();
        }

        var result = await userManager.ConfirmEmailAsync(user, decodedCode);
        Succeeded = result.Succeeded;
        return Page();
    }
}
