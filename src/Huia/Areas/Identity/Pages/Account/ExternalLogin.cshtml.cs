using Huia.Common;
using Huia.Core;
using Huia.Eventing;
using Huia.Identity;
using Huia.Localization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;

namespace Huia.Areas.Identity.Pages.Account;

/// <summary>
/// Starts (<see cref="OnPostAsync"/>) and completes (<see cref="OnGetCallbackAsync"/>) an external
/// (third-party) sign-in, registered via <c>huia.ExternalLogins</c>. Not meant to be navigated to directly;
/// reached by posting from a provider button on <see cref="LoginModel"/>'s page. A first-time identity whose
/// provider already verified its email and supplied both names is auto-provisioned here with no extra click;
/// anything less complete falls through to <see cref="ExternalLoginConfirmationModel"/> instead.
/// </summary>
[AllowAnonymous]
public class ExternalLoginModel(
    SignInManager<HuiaUser> signInManager,
    UserManager<HuiaUser> userManager,
    IEventPublisher events,
    IOpenIddictApplicationManager applicationManager,
    HuiaOptions options,
    IStringLocalizer<HuiaResources> localizer,
    ILogger<ExternalLoginModel> logger) : PageModel
{
    /// <summary>Redirects here directly (no pending external sign-in) back to <c>Login</c>.</summary>
    public IActionResult OnGet() => RedirectToPage("./Login");

    /// <summary>Challenges the requested <paramref name="provider"/>, resuming at <see cref="OnGetCallbackAsync"/>.</summary>
    public IActionResult OnPostAsync(string provider, string? returnUrl = null)
    {
        var redirectUrl = Url.Page("./ExternalLogin", "Callback", new { returnUrl });
        var properties = signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
        return Challenge(properties, [provider]);
    }

    /// <summary>Completes the external sign-in the provider redirected back from.</summary>
    // One line over MA0051's default 60-line limit; splitting this single linear callback flow wouldn't
    // reduce its actual complexity, just spread it across an extra method.
#pragma warning disable MA0051
    public async Task<IActionResult> OnGetCallbackAsync(string? returnUrl = null, string? remoteError = null)
    {
        returnUrl ??= Url.Content("~/");

        if (remoteError is not null)
        {
            TempData["ExternalLoginError"] = (string)localizer["ExternalProviderErrorMessage", remoteError];
            return RedirectToPage("./Login", new { returnUrl });
        }

        var info = await signInManager.GetExternalLoginInfoAsync();
        if (info is null)
        {
            TempData["ExternalLoginError"] = (string)localizer["ExternalLoginInfoMissingError"];
            return RedirectToPage("./Login", new { returnUrl });
        }

        var result = await signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey,
            isPersistent: false, bypassTwoFactor: false);

        if (result.Succeeded)
        {
            await signInManager.UpdateExternalAuthenticationTokensAsync(info);

            var user = await userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
            if (user is not null)
            {
                logger.LogInformation("User signed in with {LoginProvider}.", info.LoginProvider);
                await events.PublishAsync(new UserSignedInEvent<string>(user.Id, user.Email!));
            }

            // Consumed: clears the short-lived external cookie so the same provider round trip can't be
            // replayed.
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

            return Redirect(await ReturnUrlValidator.ResolveAsync(Request, returnUrl, applicationManager));
        }

        if (result.RequiresTwoFactor)
        {
            // The linked user is already established; the rest of sign-in tracks via LoginWith2fa's own
            // TwoFactorUserIdScheme cookie from here, so the external cookie's job is done too.
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            return RedirectToPage("./LoginWith2fa", new { ReturnUrl = returnUrl, RememberMe = false });
        }

        if (result.IsLockedOut)
        {
            return RedirectToPage("./Lockout");
        }

        // No local account is linked to this external identity yet. If an account already exists for the
        // email the provider reports, don't silently auto-link it — a forged/compromised external claim
        // shouldn't gain access to an existing password account on the strength of an unverified email
        // match alone. ext.EnablePasswordLinking() (inside huia.Authentication.UseExternalAuthenticationFlow)
        // opts into a middle ground: link it once the user proves ownership by entering that account's
        // actual password (see ExternalLoginLinkConfirmationModel); otherwise the default is to send them to
        // sign in normally and link the provider from account settings instead (see
        // ManageExternalLoginsEndpoints).
        var email = ExternalClaimsMapper.GetEmail(info.Principal);
        if (email is not null && await userManager.FindByEmailAsync(email) is not null)
        {
            if (options.Authentication.ExternalLoginPasswordLinkingEnabled)
            {
                return RedirectToPage("./ExternalLoginLinkConfirmation", new { returnUrl });
            }

            TempData["ExternalLoginError"] =
                (string)localizer["ExternalLoginEmailCollisionError", email, info.ProviderDisplayName!];
            return RedirectToPage("./Login", new { returnUrl });
        }

        // Skip the extra click on ExternalLoginConfirmation when the provider already gave us everything a
        // new account needs and vouched for the email itself (reliable for Google/Microsoft; not guaranteed
        // for a generic AddOAuth registration — see ExternalClaimsMapper). Anything short of that — a missing
        // name, an unverified email, or account creation itself failing — falls through to the confirmation
        // page below, which has the UI to collect what's missing or surface why it failed.
        if (email is not null && ExternalClaimsMapper.IsEmailVerified(info.Principal))
        {
            var firstName = ExternalClaimsMapper.GetFirstName(info.Principal);
            var lastName = ExternalClaimsMapper.GetLastName(info.Principal);

            if (!string.IsNullOrEmpty(firstName) && !string.IsNullOrEmpty(lastName))
            {
                var autoProvisioned = await TryAutoProvisionAsync(info, email, firstName, lastName, returnUrl);
                if (autoProvisioned is not null)
                {
                    return autoProvisioned;
                }
            }
        }

        return RedirectToPage("./ExternalLoginConfirmation", new { returnUrl });
    }
#pragma warning restore MA0051

    /// <summary>
    /// Creates the account and signs in immediately, mirroring <c>ExternalLoginConfirmationModel</c>'s own
    /// verified-email path (see <see cref="ExternalAccountFactory"/>) — never the deferred-email-confirmation
    /// branch, since this is only ever called once the provider has already verified the email. Returns
    /// <see langword="null"/> on any creation failure so the caller can fall back to the confirmation page,
    /// which — unlike this page — has ModelState/a form to actually show the error on.
    /// </summary>
    private async Task<IActionResult?> TryAutoProvisionAsync(ExternalLoginInfo info, string email, string firstName,
        string lastName, string returnUrl)
    {
        var (user, errors) = await ExternalAccountFactory.CreateAsync(userManager, info, email, firstName, lastName,
            emailConfirmed: true);
        if (user is null)
        {
            logger.LogWarning("Auto-provisioning via {LoginProvider} failed: {Errors}", info.LoginProvider,
                string.Join("; ", errors));
            return null;
        }

        logger.LogInformation("User created a new account via {LoginProvider}.", info.LoginProvider);

        var userId = await userManager.GetUserIdAsync(user);
        await events.PublishAsync(new UserRegisteredEvent<string>(userId, email));

        await signInManager.SignInAsync(user, isPersistent: false);
        await events.PublishAsync(new UserSignedInEvent<string>(userId, email));

        // Consumed: clears the short-lived external cookie so the same provider round trip can't be replayed
        // to link/create a second account.
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

        return Redirect(await ReturnUrlValidator.ResolveAsync(Request, returnUrl, applicationManager));
    }
}
