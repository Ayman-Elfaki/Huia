using System.ComponentModel.DataAnnotations;
using System.Text;
using Huia.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace Huia.Areas.Identity.Pages.Account;

/// <summary>Requests a password reset email for a given address.</summary>
[AllowAnonymous]
public class ForgotPasswordModel(UserManager<HuiaUser> userManager, IEmailSender<HuiaUser> emailSender) : PageModel
{
    /// <summary>The submitted form data.</summary>
    [BindProperty]
    public InputModel Input { get; set; } = new();

    /// <summary>
    /// Where to send the user once they sign in — carried through to <c>Login</c> and the emailed reset
    /// link, so an in-progress OAuth flow isn't lost while the user resets their password.
    /// </summary>
    public string? ReturnUrl { get; set; }

    /// <summary>Renders the page.</summary>
    public void OnGet(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
    }

    /// <summary>Sends a password reset email if the address belongs to a confirmed account.</summary>
    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await userManager.FindByEmailAsync(Input.Email);
        if (user is not null && await userManager.IsEmailConfirmedAsync(user))
        {
            var code = await userManager.GeneratePasswordResetTokenAsync(user);
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
            var resetLink = Url.PageLink("./ResetPassword", values: new { code, returnUrl = ReturnUrl }, protocol: Request.Scheme)
                ?? throw new InvalidOperationException("The password reset link could not be generated.");
            await emailSender.SendPasswordResetLinkAsync(user, Input.Email, resetLink);
        }

        // Always redirect to the same confirmation, whether or not the address is registered.
        return RedirectToPage("./ForgotPasswordConfirmation", new { returnUrl = ReturnUrl });
    }

    /// <summary>The forgot-password form fields.</summary>
    public sealed class InputModel
    {
        /// <summary>The email address to send the reset link to.</summary>
        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        public string Email { get; set; } = string.Empty;
    }
}
