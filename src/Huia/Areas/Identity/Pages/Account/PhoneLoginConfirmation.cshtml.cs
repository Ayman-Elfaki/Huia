using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Huia.Common;
using Huia.Eventing;
using Huia.Identity;
using Huia.Passwordless;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;

namespace Huia.Areas.Identity.Pages.Account;

/// <summary>
/// Collects a name to finish setting up a brand-new passwordless-phone account — reached from
/// <see cref="PhoneLoginVerifyModel.OnPostAsync"/> only after the OTP has actually been verified (the
/// <c>Huia.PhoneVerification</c> cookie's <c>verified</c> claim proves that; this page can't be reached by
/// forging/replaying a cookie that never passed verification). Publishes
/// <see cref="UserRegisteredEvent{TKey}"/> and <see cref="UserSignedInEvent{TKey}"/> — the same events
/// <c>RegisterModel</c>/<c>ExternalLoginConfirmationModel</c> publish.
/// </summary>
[AllowAnonymous]
public class PhoneLoginConfirmationModel(
    UserManager<HuiaUser> userManager,
    SignInManager<HuiaUser> signInManager,
    IEventPublisher events,
    IOpenIddictApplicationManager applicationManager,
    ILogger<PhoneLoginConfirmationModel> logger) : PageModel
{
    /// <summary>The submitted form data.</summary>
    [BindProperty]
    public InputModel Input { get; set; } = new();

    /// <summary>Where to redirect after the account is confirmed.</summary>
    public string ReturnUrl { get; set; } = "~/";

    /// <summary>Renders the page, or back to <c>PhoneLogin</c> if no verified sign-in is actually pending.</summary>
    public async Task<IActionResult> OnGetAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl ?? Url.Content("~/");

        return await AuthenticateVerifiedAsync() is null
            ? RedirectToPage("./PhoneLogin")
            : Page();
    }

    /// <summary>Sets the account's name and completes sign-in.</summary>
    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl ?? Url.Content("~/");

        // Re-fetched, not trusted from the posted form: which account this completes must come from the
        // still-live, already-OTP-verified cookie, never from a client-supplied field.
        var userId = await AuthenticateVerifiedAsync();
        if (userId is null)
        {
            return RedirectToPage("./PhoneLogin");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            await HttpContext.SignOutAsync(PhoneVerificationScheme.Name);
            return RedirectToPage("./PhoneLogin");
        }

        user.FirstName = Input.FirstName;
        user.LastName = Input.LastName;

        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            foreach (var error in updateResult.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return Page();
        }

        logger.LogInformation("User completed passwordless phone registration.");
        await events.PublishAsync(new UserRegisteredEvent<string>(user.Id, user.Email));

        await signInManager.SignInAsync(user, isPersistent: false);
        await events.PublishAsync(new UserSignedInEvent<string>(user.Id, user.Email));

        // Consumed: clears the short-lived phone-verification cookie so the same verified state can't be
        // replayed to reach this page again.
        await HttpContext.SignOutAsync(PhoneVerificationScheme.Name);

        return Redirect(await ReturnUrlValidator.ResolveAsync(Request, ReturnUrl, applicationManager));
    }

    private async Task<string?> AuthenticateVerifiedAsync()
    {
        var result = await HttpContext.AuthenticateAsync(PhoneVerificationScheme.Name);
        if (!result.Succeeded || result.Principal is null)
        {
            return null;
        }

        var verified = result.Principal.FindFirstValue(PhoneVerificationScheme.VerifiedClaimType);
        return string.Equals(verified, "true", StringComparison.Ordinal)
            ? result.Principal.FindFirstValue(PhoneVerificationScheme.UserIdClaimType)
            : null;
    }

    /// <summary>The confirmation form fields.</summary>
    public sealed class InputModel
    {
        /// <summary>The new account's given (first) name.</summary>
        [Required(ErrorMessage = "First name is required.")]
        [Display(Name = "First name")]
        public string FirstName { get; set; } = string.Empty;

        /// <summary>The new account's family (last) name.</summary>
        [Required(ErrorMessage = "Last name is required.")]
        [Display(Name = "Last name")]
        public string LastName { get; set; } = string.Empty;
    }
}
