using System.ComponentModel.DataAnnotations;
using System.Text;
using Huia.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace Huia.Areas.Identity.Pages.Account;

/// <summary>Resets a user's password from the token sent by the forgot-password flow.</summary>
[AllowAnonymous]
public class ResetPasswordModel(UserManager<HuiaUser> userManager) : PageModel
{
    /// <summary>The submitted form data.</summary>
    [BindProperty]
    public InputModel Input { get; set; } = new();

    /// <summary>
    /// Where to send the user once they sign in — carried through from the link <c>ForgotPassword</c>
    /// emailed them, so an in-progress OAuth flow isn't lost while the user resets their password.
    /// </summary>
    public string? ReturnUrl { get; set; }

    /// <summary>Renders the page, decoding the reset <paramref name="code"/> into <see cref="InputModel.Code"/>.</summary>
    public IActionResult OnGet(string? code = null, string? returnUrl = null)
    {
        ReturnUrl = returnUrl;

        if (code is null)
        {
            return BadRequest("A password reset code must be supplied for this request.");
        }

        Input = new InputModel { Code = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code)) };
        return Page();
    }

    /// <summary>Resets the password if the code is valid; always redirects to the confirmation page.</summary>
    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await userManager.FindByEmailAsync(Input.Email);
        if (user is null)
        {
            // Don't reveal whether the address is registered.
            return RedirectToPage("./ResetPasswordConfirmation", new { returnUrl = ReturnUrl });
        }

        var result = await userManager.ResetPasswordAsync(user, Input.Code, Input.Password);
        if (result.Succeeded)
        {
            return RedirectToPage("./ResetPasswordConfirmation", new { returnUrl = ReturnUrl });
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }

        return Page();
    }

    /// <summary>The password reset form fields.</summary>
    public sealed class InputModel
    {
        /// <summary>The account's email address.</summary>
        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        public string Email { get; set; } = string.Empty;

        /// <summary>The new password.</summary>
        [Required(ErrorMessage = "Password is required.")]
        [StringLength(100, ErrorMessage = "{0} must be at least {2} characters long.", MinimumLength = 6)]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        /// <summary>Must match <see cref="Password"/>.</summary>
        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare(nameof(Password), ErrorMessage = "The password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;

        /// <summary>The password reset token from the emailed link.</summary>
        [Required]
        public string Code { get; set; } = string.Empty;
    }
}
