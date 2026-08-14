using System.ComponentModel.DataAnnotations;
using System.Text;
using Huia.Common;
using Huia.Eventing;
using Huia.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;

namespace Huia.Areas.Identity.Pages.Account;

/// <summary>
/// Explicit-consent account creation for a first-time external sign-in — reached from
/// <see cref="ExternalLoginModel.OnGetCallbackAsync"/> when the provider's identity isn't linked to any
/// local account yet. Creates the account (no password) and links the external login, publishing
/// <see cref="UserRegisteredEvent{TKey}"/> and (if email confirmation isn't required)
/// <see cref="UserSignedInEvent{TKey}"/> — the same events <c>RegisterModel</c> publishes.
/// </summary>
[AllowAnonymous]
public class ExternalLoginConfirmationModel(
    UserManager<HuiaUser> userManager,
    SignInManager<HuiaUser> signInManager,
    IEmailSender<HuiaUser> emailSender,
    IEventPublisher events,
    IOpenIddictApplicationManager applicationManager,
    ILogger<ExternalLoginConfirmationModel> logger) : PageModel
{
    /// <summary>The submitted form data.</summary>
    [BindProperty]
    public InputModel Input { get; set; } = new();

    /// <summary>Where to redirect after the account is created.</summary>
    public string ReturnUrl { get; set; } = "~/";

    /// <summary>The provider the pending external identity came from, for display.</summary>
    public string ProviderDisplayName { get; set; } = string.Empty;

    /// <summary>Renders the page, pre-filled from the pending external identity's claims.</summary>
    public async Task<IActionResult> OnGetAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl ?? Url.Content("~/");

        var info = await signInManager.GetExternalLoginInfoAsync();
        if (info is null)
        {
            TempData["ExternalLoginError"] = "Error loading external login information during confirmation.";
            return RedirectToPage("./Login", new { ReturnUrl });
        }

        ProviderDisplayName = info.ProviderDisplayName ?? info.LoginProvider;
        Input.Email = ExternalClaimsMapper.GetEmail(info.Principal) ?? string.Empty;
        Input.FirstName = ExternalClaimsMapper.GetFirstName(info.Principal) ?? string.Empty;
        Input.LastName = ExternalClaimsMapper.GetLastName(info.Principal) ?? string.Empty;

        return Page();
    }

    /// <summary>
    /// Creates the account, links the external login, and either sends a confirmation email or
    /// signs the user in immediately.
    /// </summary>
    // One line over MA0051's default 60-line limit; splitting this single linear confirmation flow wouldn't
    // reduce its actual complexity, just spread it across an extra method.
#pragma warning disable MA0051
    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl ?? Url.Content("~/");

        // Re-fetched, not trusted from the posted form: the external identity must come from the still-live
        // external-scheme cookie, never from client-supplied fields, or a forged POST could bind an
        // attacker-chosen external identity to whatever account this handler ends up creating.
        var info = await signInManager.GetExternalLoginInfoAsync();
        if (info is null)
        {
            TempData["ExternalLoginError"] = "Error loading external login information during confirmation.";
            return RedirectToPage("./Login", new { ReturnUrl });
        }

        ProviderDisplayName = info.ProviderDisplayName ?? info.LoginProvider;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (await userManager.FindByEmailAsync(Input.Email) is not null)
        {
            ModelState.AddModelError(string.Empty,
                $"An account already exists for {Input.Email}. Sign in with your password, then link {ProviderDisplayName} from your account settings.");
            return Page();
        }

        var user = new HuiaUser
        {
            UserName = Input.Email,
            Email = Input.Email,
            FirstName = Input.FirstName,
            LastName = Input.LastName,
            Picture = ExternalClaimsMapper.GetPicture(info.Principal),
        };

        var createResult = await userManager.CreateAsync(user);
        if (!createResult.Succeeded)
        {
            foreach (var error in createResult.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return Page();
        }

        var addLoginResult = await userManager.AddLoginAsync(user, info);
        if (!addLoginResult.Succeeded)
        {
            foreach (var error in addLoginResult.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return Page();
        }

        logger.LogInformation("User created a new account via {LoginProvider}.", info.LoginProvider);

        var userId = await userManager.GetUserIdAsync(user);
        await events.PublishAsync(new UserRegisteredEvent<string>(userId, Input.Email));

        var emailVerifiedByProvider = ExternalClaimsMapper.IsEmailVerified(info.Principal);
        if (emailVerifiedByProvider)
        {
            user.EmailConfirmed = true;
            await userManager.UpdateAsync(user);
        }

        if (!emailVerifiedByProvider && userManager.Options.SignIn.RequireConfirmedAccount)
        {
            var code = await userManager.GenerateEmailConfirmationTokenAsync(user);
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));

            var confirmationLink = Url.PageLink("./ConfirmEmail",
                                       values: new { userId, code, returnUrl = ReturnUrl },
                                       protocol: Request.Scheme)
                                   ?? throw new InvalidOperationException(
                                       "The email confirmation link could not be generated.");

            await emailSender.SendConfirmationLinkAsync(user, Input.Email, confirmationLink);

            return RedirectToPage("RegisterConfirmation", new { email = Input.Email, returnUrl = ReturnUrl });
        }

        await signInManager.SignInAsync(user, isPersistent: false);
        await events.PublishAsync(new UserSignedInEvent<string>(userId, Input.Email));

        // Consumed: clears the short-lived external cookie so the same provider round trip can't be replayed
        // to link/create a second account.
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

        return Redirect(await ReturnUrlValidator.ResolveAsync(Request, ReturnUrl, applicationManager));
    }
#pragma warning restore MA0051

    /// <summary>
    /// The confirmation form fields, pre-filled from the external provider's claims where available.
    /// </summary>
    public sealed class InputModel
    {
        /// <summary>
        /// The new account's given (first) name.
        /// </summary>
        [Required(ErrorMessage = "First name is required.")]
        [Display(Name = "First name")]
        public string FirstName { get; set; } = string.Empty;

        /// <summary>
        /// The new account's family (last) name.
        /// </summary>
        [Required(ErrorMessage = "Last name is required.")]
        [Display(Name = "Last name")]
        public string LastName { get; set; } = string.Empty;

        /// <summary>
        /// The new account's email address (also used as the username).
        /// </summary>
        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        public string Email { get; set; } = string.Empty;
    }
}
