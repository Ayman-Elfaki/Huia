using System.ComponentModel.DataAnnotations;
using System.Text;
using Huia.Applications;
using Huia.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using OpenIddict.Abstractions;

namespace Huia.Areas.Identity.Pages.Account;

/// <summary>
/// Resends the email confirmation link for an unconfirmed account.
/// </summary>
[AllowAnonymous]
public class ResendEmailConfirmationModel(
    UserManager<HuiaUser> userManager,
    IEmailSender<HuiaUser> emailSender,
    IOpenIddictApplicationManager applicationManager) : PageModel
{
    /// <summary>
    /// The submitted form data.
    /// </summary>
    [BindProperty]
    public InputModel Input { get; set; } = new();

    /// <summary>
    /// Whether the form has been submitted (controls the confirmation message).
    /// </summary>
    public bool Submitted { get; private set; }

    /// <summary>
    /// Where to send the user once they sign in — carried through to the emailed confirmation link so an
    /// in-progress OAuth flow isn't lost.
    /// </summary>
    public string? ReturnUrl { get; set; }

    /// <summary>
    /// Renders the page.
    /// </summary>
    public void OnGet(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
    }

    /// <summary>
    /// Resends the confirmation email if the address exists and isn't already confirmed.
    /// </summary>
    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await userManager.FindByEmailAsync(Input.Email);
        if (user is not null && !await userManager.IsEmailConfirmedAsync(user))
        {
            var userId = await userManager.GetUserIdAsync(user);
            var code = await userManager.GenerateEmailConfirmationTokenAsync(user);
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));

            // Same reasoning as Register.cshtml.cs: only a still-pending sign-in (RequireConfirmedAccount)
            // can safely replay the original /connect/authorize returnUrl later — otherwise resolve to the
            // client's HomeUri/origin (see ClientHomeResolver) so the confirmation link's "Sign in" lands
            // back on the actual application instead of a dead PKCE replay or Huia's own bare root.
            var confirmationReturnUrl = userManager.Options.SignIn.RequireConfirmedAccount
                ? ReturnUrl
                : await ApplicationHomeUriResolver.ResolveFromAuthorizeReturnUrlAsync(ReturnUrl, applicationManager);
            var confirmationLink = Url.PageLink("./ConfirmEmail",
                                       values: new { userId, code, returnUrl = confirmationReturnUrl },
                                       protocol: Request.Scheme)
                                   ?? throw new InvalidOperationException(
                                       "The email confirmation link could not be generated.");
            await emailSender.SendConfirmationLinkAsync(user, Input.Email, confirmationLink);
        }

        // Always show the same message: don't reveal whether the address is registered.
        Submitted = true;
        return Page();
    }

    /// <summary>
    /// The resend-confirmation form fields.
    /// </summary>
    public sealed class InputModel
    {
        /// <summary>
        /// The account's email address.
        /// </summary>
        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        public string Email { get; set; } = string.Empty;
    }
}