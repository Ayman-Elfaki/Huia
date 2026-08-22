using System.ComponentModel.DataAnnotations;
using Huia.Eventing;
using Huia.Common;
using Huia.Identity;
using Huia.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;

namespace Huia.Areas.Identity.Pages.Account;

/// <summary>Completes sign-in for a user whose password check succeeded but who has 2FA enabled.</summary>
[AllowAnonymous]
public class LoginWith2faModel(
    HuiaSignInManager signInManager,
    IEventPublisher events,
    IOpenIddictApplicationManager applicationManager,
    IStringLocalizer<HuiaResources> localizer,
    ILogger<LoginWith2faModel> logger) : PageModel
{
    /// <summary>The submitted form data.</summary>
    [BindProperty]
    public InputModel Input { get; set; } = new();

    /// <summary>Whether the original password sign-in requested a persistent cookie.</summary>
    public bool RememberMe { get; set; }

    /// <summary>Where to redirect after a successful sign-in.</summary>
    public string ReturnUrl { get; set; } = "~/";

    /// <summary>Renders the page, verifying a two-factor sign-in is actually pending.</summary>
    public async Task<IActionResult> OnGetAsync(bool rememberMe, string? returnUrl = null)
    {
        _ = await signInManager.GetTwoFactorAuthenticationUserAsync()
            ?? throw new InvalidOperationException("Unable to load two-factor authentication user.");

        RememberMe = rememberMe;
        ReturnUrl = returnUrl ?? Url.Content("~/");
        return Page();
    }

    /// <summary>Verifies the authenticator code and completes sign-in.</summary>
    public async Task<IActionResult> OnPostAsync(bool rememberMe, string? returnUrl = null)
    {
        if (!ModelState.IsValid)
        {
            RememberMe = rememberMe;
            ReturnUrl = returnUrl ?? Url.Content("~/");
            return Page();
        }

        ReturnUrl = returnUrl ?? Url.Content("~/");

        var user = await signInManager.GetTwoFactorAuthenticationUserAsync()
            ?? throw new InvalidOperationException("Unable to load two-factor authentication user.");

        var authenticatorCode = Input.TwoFactorCode.Replace(" ", string.Empty).Replace("-", string.Empty);
        var result = await signInManager.TwoFactorAuthenticatorSignInAsync(authenticatorCode, rememberMe, Input.RememberMachine);

        if (result.Succeeded)
        {
            logger.LogInformation("User with ID '{UserId}' logged in with 2fa.", user.Id);
            await events.PublishAsync(new UserSignedInEvent<string>(user.Id));
            return Redirect(await ReturnUrlValidator.ResolveAsync(Request, ReturnUrl, applicationManager));
        }

        if (result.IsLockedOut)
        {
            return RedirectToPage("./Lockout");
        }

        ModelState.AddModelError(string.Empty, localizer["InvalidAuthenticatorCodeError"]);
        return Page();
    }

    /// <summary>The two-factor verification form fields.</summary>
    public sealed class InputModel
    {
        /// <summary>The one-time code from the user's authenticator app.</summary>
        [Required(ErrorMessage = "Authenticator code is required.")]
        [Display(Name = "Authenticator code")]
        public string TwoFactorCode { get; set; } = string.Empty;

        /// <summary>Whether to skip 2FA on this browser/device for future sign-ins.</summary>
        [Display(Name = "Remember this machine")]
        public bool RememberMachine { get; set; }
    }
}
