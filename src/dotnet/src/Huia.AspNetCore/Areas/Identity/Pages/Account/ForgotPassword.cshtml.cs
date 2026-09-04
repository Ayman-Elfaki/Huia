using System.ComponentModel.DataAnnotations;
using System.Text;
using Huia.AspNetCore.Emails;
using Huia.AspNetCore.Flows;
using Huia.AspNetCore.Identity;
using Huia.AspNetCore.UI;
using Huia.EntityFrameworkCore.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Localization;

namespace Huia.AspNetCore.Areas.Identity.Pages.Account;

/// <summary>
/// Starts a password-reset. Always reports success to avoid disclosing which addresses have accounts;
/// the reset email is only sent when a matching confirmed account exists.
/// </summary>
public sealed class ForgotPasswordModel(
    IHuiaFlowIdentityFactory flowIdentity,
    IHuiaEmailSender emailSender,
    IReturnUrlProtector returnUrlProtector,
    IStringLocalizer<SharedResource> localizer) : HuiaAccountPageModel
{
    /// <summary>The bound email address.</summary>
    [BindProperty]
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// The sanitized return URL carried through to the "Sign in" link, so an in-progress OAuth
    /// authorize request survives a detour through password recovery.
    /// </summary>
    public string ReturnUrl { get; private set; } = "/";

    /// <summary>Whether the confirmation panel should be shown.</summary>
    public bool Submitted { get; private set; }

    /// <summary>Handles the initial GET.</summary>
    /// <param name="returnUrl">The URL to return to after signing in.</param>
    public void OnGet(string? returnUrl)
    {
        SetHeadings();
        ReturnUrl = returnUrlProtector.SanitizeReturnUrl(returnUrl, HttpContext);
    }

    /// <summary>Handles the POST. Always succeeds from the caller's point of view.</summary>
    /// <param name="returnUrl">The URL to return to after signing in.</param>
    /// <returns>The page with the confirmation panel.</returns>
    public async Task<IActionResult> OnPostAsync(string? returnUrl)
    {
        SetHeadings();
        ReturnUrl = returnUrlProtector.SanitizeReturnUrl(returnUrl, HttpContext);
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var userManager = flowIdentity.Create(HuiaAuthFlow.EmailAndPasswordLogin).UserManager;
        var user = await userManager.FindByEmailAsync(Email);
        if (user is not null && await userManager.IsEmailConfirmedAsync(user))
        {
            var rawToken = await userManager.GeneratePasswordResetTokenAsync(user);
            var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(rawToken));
            var resetUrl = Url.Page("./ResetPassword", pageHandler: null,
                values: new { userId = user.Id, code }, protocol: Request.Scheme)!;
            await emailSender.SendPasswordResetAsync(user, resetUrl, HttpContext.RequestAborted);
        }

        Submitted = true;
        return Page();
    }

    private void SetHeadings()
    {
        ViewData["Title"] = localizer["ForgotPassword.Title"].Value;
        ViewData["Heading"] = localizer["ForgotPassword.Heading"].Value;
    }
}
