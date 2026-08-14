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
/// Starts (<see cref="OnPostAsync"/>) and completes (<see cref="OnGetCallbackAsync"/>) an external
/// (third-party) sign-in, registered via <c>huia.ExternalLogins</c>. Not meant to be navigated to directly;
/// reached by posting from a provider button on <see cref="LoginModel"/>'s page.
/// </summary>
[AllowAnonymous]
public class ExternalLoginModel(
    SignInManager<HuiaUser> signInManager,
    UserManager<HuiaUser> userManager,
    IEventPublisher events,
    IOpenIddictApplicationManager applicationManager,
    HuiaOptions options,
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
            TempData["ExternalLoginError"] = $"Error from external provider: {remoteError}";
            return RedirectToPage("./Login", new { returnUrl });
        }

        var info = await signInManager.GetExternalLoginInfoAsync();
        if (info is null)
        {
            TempData["ExternalLoginError"] = "Error loading external login information.";
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
        // match alone. huia.EnableExternalLoginPasswordLinking() opts into a middle ground: link it once the
        // user proves ownership by entering that account's actual password (see
        // ExternalLoginLinkConfirmationModel); otherwise the default is to send them to sign in normally and
        // link the provider from account settings instead (see ManageExternalLoginsEndpoints).
        var email = ExternalClaimsMapper.GetEmail(info.Principal);
        if (email is not null && await userManager.FindByEmailAsync(email) is not null)
        {
            if (options.ExternalLoginPasswordLinkingEnabled)
            {
                return RedirectToPage("./ExternalLoginLinkConfirmation", new { returnUrl });
            }

            TempData["ExternalLoginError"] =
                $"An account already exists for {email}. Sign in with your password, then link {info.ProviderDisplayName} from your account settings.";
            return RedirectToPage("./Login", new { returnUrl });
        }

        return RedirectToPage("./ExternalLoginConfirmation", new { returnUrl });
    }
#pragma warning restore MA0051
}
