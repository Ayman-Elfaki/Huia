using System.ComponentModel.DataAnnotations;
using Huia.Eventing;
using Huia.Common;
using Huia.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;

namespace Huia.Areas.Identity.Pages.Account;

/// <summary>Signs a user in with email/password, publishing <see cref="UserSignedInEvent"/> on success.</summary>
[AllowAnonymous]
public class LoginModel(
    HuiaSignInManager signInManager,
    UserManager<HuiaUser> userManager,
    IEventPublisher events,
    IOpenIddictApplicationManager applicationManager,
    ILogger<LoginModel> logger) : PageModel
{
    /// <summary>The submitted form data.</summary>
    [BindProperty]
    public InputModel Input { get; set; } = new();

    /// <summary>Where to redirect after a successful sign-in.</summary>
    public string ReturnUrl { get; set; } = "~/";

    /// <summary>
    /// Renders the page, or — if the user is already signed in (e.g. another browser tab just completed a
    /// sign-in that this one's cross-tab sync script noticed, see <c>huia-session-sync.js</c>) — redirects
    /// straight to <paramref name="returnUrl"/> instead of showing a stale login form.
    /// </summary>
    public async Task<IActionResult> OnGetAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl ?? Url.Content("~/");

        // HttpContext.User (the default-scheme principal) isn't the sign-in cookie here — Huia's default
        // authentication scheme is the OpenIddict bearer validator (see ServiceCollectionExtensions), so
        // this authenticates against the Identity cookie scheme explicitly, the same way
        // AuthorizationEndpoints checks for an existing session.
        var signedIn = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);

        return signedIn.Succeeded
            ? Redirect(await ReturnUrlValidator.ResolveAsync(Request, ReturnUrl, applicationManager))
            : Page();
    }

    /// <summary>Attempts sign-in, redirecting to 2FA/lockout pages or <paramref name="returnUrl"/> as appropriate.</summary>
    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl ?? Url.Content("~/");

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result =
            await signInManager.PasswordSignInAsync(Input.Email, Input.Password, Input.RememberMe,
                lockoutOnFailure: true);

        if (result.Succeeded)
        {
            logger.LogInformation("User signed in.");

            var user = await userManager.FindByEmailAsync(Input.Email);
            if (user is not null)
            {
                await events.PublishAsync(new UserSignedInEvent(user.Id, Input.Email, signInManager.CurrentSessionId!));
            }

            return Redirect(await ReturnUrlValidator.ResolveAsync(Request, ReturnUrl, applicationManager));
        }

        if (result.RequiresTwoFactor)
        {
            return RedirectToPage("./LoginWith2fa", new { ReturnUrl, Input.RememberMe });
        }

        if (result.IsLockedOut)
        {
            return RedirectToPage("./Lockout");
        }

        ModelState.AddModelError(string.Empty, "Invalid email or password.");
        return Page();
    }

    /// <summary>The sign-in form fields.</summary>
    public sealed class InputModel
    {
        /// <summary>The account's email address.</summary>
        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        public string Email { get; set; } = string.Empty;

        /// <summary>The account's password.</summary>
        [Required(ErrorMessage = "Password is required.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        /// <summary>Whether to issue a persistent (as opposed to session) sign-in cookie.</summary>
        [Display(Name = "Remember me")]
        public bool RememberMe { get; set; }
    }
}