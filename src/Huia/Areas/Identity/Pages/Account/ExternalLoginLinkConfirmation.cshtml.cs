using System.ComponentModel.DataAnnotations;
using Huia.Common;
using Huia.Core;
using Huia.Eventing;
using Huia.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;

namespace Huia.Areas.Identity.Pages.Account;

/// <summary>
/// Reached from <see cref="ExternalLoginModel.OnGetCallbackAsync"/> only when
/// <c>huia.EnableExternalLoginPasswordLinking()</c> is on and the external provider's email collides with an
/// existing password account. Links the external identity to that account once the user proves ownership by
/// entering its password — via the same <c>PasswordSignInAsync</c> overload the normal login form uses
/// (lockout tracking and 2FA routing included, not <c>CheckPasswordSignInAsync</c>, which only checks the
/// password and skips 2FA entirely) — rather than requiring a separate sign-in-then-link-from-settings round
/// trip.
/// </summary>
[AllowAnonymous]
public class ExternalLoginLinkConfirmationModel(
    UserManager<HuiaUser> userManager,
    SignInManager<HuiaUser> signInManager,
    IEventPublisher events,
    IOpenIddictApplicationManager applicationManager,
    HuiaOptions options,
    ILogger<ExternalLoginLinkConfirmationModel> logger) : PageModel
{
    /// <summary>The submitted form data.</summary>
    [BindProperty]
    public InputModel Input { get; set; } = new();

    /// <summary>Where to redirect after linking completes.</summary>
    public string ReturnUrl { get; set; } = "~/";

    /// <summary>The colliding account's email, for display — read-only; always taken from the pending
    /// external identity's own claims, never from user input.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>The provider the pending external identity came from, for display.</summary>
    public string ProviderDisplayName { get; set; } = string.Empty;

    /// <summary>Renders the page for the pending external identity's colliding email.</summary>
    public async Task<IActionResult> OnGetAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl ?? Url.Content("~/");

        var (info, redirect) = await LoadPendingLinkAsync(ReturnUrl);
        if (redirect is not null)
        {
            return redirect;
        }

        Email = ExternalClaimsMapper.GetEmail(info!.Principal)!;
        ProviderDisplayName = info.ProviderDisplayName ?? info.LoginProvider;

        return Page();
    }

    /// <summary>Verifies the submitted password against the colliding account, links the external identity
    /// to it, and signs in (or continues into 2FA, exactly like a normal password sign-in).</summary>
    // One line over MA0051's default 60-line limit; splitting this single linear confirmation flow wouldn't
    // reduce its actual complexity, just spread it across an extra method.
#pragma warning disable MA0051
    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl ?? Url.Content("~/");

        var (info, redirect) = await LoadPendingLinkAsync(ReturnUrl);
        if (redirect is not null)
        {
            return redirect;
        }

        Email = ExternalClaimsMapper.GetEmail(info!.Principal)!;
        ProviderDisplayName = info.ProviderDisplayName ?? info.LoginProvider;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        // Re-resolved from the external identity's own claims, not trusted from the posted form — the
        // account being linked is always the one the provider's email actually collided with.
        var user = await userManager.FindByEmailAsync(Email);
        if (user is null)
        {
            // The colliding account was deleted between the callback and this POST — fall through to normal
            // first-time registration instead of erroring.
            return RedirectToPage("./ExternalLoginConfirmation", new { ReturnUrl });
        }

        // PasswordSignInAsync(TUser, ...) — not the string-username overload the Login page uses, since the
        // account is already resolved here — verifies the password with the same lockout tracking as a
        // normal sign-in, detects a 2FA requirement, and (on outright success) signs in immediately, all in
        // one call.
        var result = await signInManager.PasswordSignInAsync(user, Input.Password, isPersistent: false,
            lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            return RedirectToPage("./Lockout");
        }

        if (!result.Succeeded && !result.RequiresTwoFactor)
        {
            ModelState.AddModelError(string.Empty, "Incorrect password.");
            return Page();
        }

        // The password alone is the proof of ownership this flow requires to link — see
        // HuiaOptions.EnableExternalLoginPasswordLinking's own doc comment. Whether 2FA still needs to run
        // afterward only affects when the session itself is established, not whether linking already happened.
        var addLoginResult = await userManager.AddLoginAsync(user, info);
        if (!addLoginResult.Succeeded)
        {
            foreach (var error in addLoginResult.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return Page();
        }

        logger.LogInformation("Linked {LoginProvider} to an existing account via password confirmation.",
            info.LoginProvider);

        if (result.RequiresTwoFactor)
        {
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            return RedirectToPage("./LoginWith2fa", new { ReturnUrl, RememberMe = false });
        }

        // Already signed in by PasswordSignInAsync above.
        await events.PublishAsync(new UserSignedInEvent(user.Id, user.Email!));
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

        return Redirect(await ReturnUrlValidator.ResolveAsync(Request, ReturnUrl, applicationManager));
    }
#pragma warning restore MA0051

    /// <summary>
    /// Common entry guard for both handlers: the feature must still be enabled, and a pending external
    /// identity with a resolvable colliding email must still be live. Returns the info to proceed with, or a
    /// redirect away from this page.
    /// </summary>
    private async Task<(ExternalLoginInfo? Info, IActionResult? Redirect)> LoadPendingLinkAsync(string returnUrl)
    {
        if (!options.ExternalLoginPasswordLinkingEnabled)
        {
            return (null, RedirectToPage("./Login", new { returnUrl }));
        }

        var info = await signInManager.GetExternalLoginInfoAsync();
        if (info is null)
        {
            TempData["ExternalLoginError"] = "Error loading external login information during confirmation.";
            return (null, RedirectToPage("./Login", new { returnUrl }));
        }

        var email = ExternalClaimsMapper.GetEmail(info.Principal);
        if (email is null || await userManager.FindByEmailAsync(email) is null)
        {
            return (null, RedirectToPage("./ExternalLoginConfirmation", new { returnUrl }));
        }

        return (info, null);
    }

    /// <summary>The link-confirmation form fields.</summary>
    public sealed class InputModel
    {
        /// <summary>The colliding account's password.</summary>
        [Required(ErrorMessage = "Password is required.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;
    }
}
