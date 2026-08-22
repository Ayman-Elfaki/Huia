using System.ComponentModel.DataAnnotations;
using System.Text;
using Huia.Applications;
using Huia.Emails;
using Huia.Eventing;
using Huia.Common;
using Huia.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;

namespace Huia.Areas.Identity.Pages.Account;

/// <summary>
/// Registers a new account, publishing <see cref="UserRegisteredEvent{TKey}"/> and (if email confirmation isn't required) <see cref="UserSignedInEvent{TKey}"/>.
/// Unreachable (404) when <c>huia.DisableRegistration()</c> was called.
/// </summary>
[AllowAnonymous]
public class RegisterModel(
    HuiaUserManager userManager,
    HuiaSignInManager signInManager,
    IEmailSender<HuiaUser> emailSender,
    IEventPublisher events,
    IOpenIddictApplicationManager applicationManager,
    HuiaOptions options,
    ILogger<RegisterModel> logger) : PageModel
{
    /// <summary>The submitted form data.</summary>
    [BindProperty]
    public InputModel Input { get; set; } = new();

    /// <summary>Where to redirect after registration completes.</summary>
    public string ReturnUrl { get; set; } = "~/";

    /// <summary>
    /// Renders the page, or — if the user is already signed in (e.g. another browser tab just completed a
    /// sign-in that this one's cross-tab sync script noticed, see <c>huia-session-sync.js</c>) — redirects
    /// straight to <paramref name="returnUrl"/> instead of showing a stale registration form.
    /// </summary>
    public async Task<IActionResult> OnGetAsync(string? returnUrl = null)
    {
        if (!options.RegistrationEnabled)
        {
            return NotFound();
        }

        ReturnUrl = returnUrl ?? Url.Content("~/");

        // HttpContext.User (the default-scheme principal) isn't the sign-in cookie here — Huia's default
        // authentication scheme is the OpenIddict bearer validator (see ServiceCollectionExtensions), so
        // this authenticates against the Identity cookie scheme explicitly, the same way
        // AuthorizationEndpoints checks for an existing session.
        var signedIn = await HttpContext.AuthenticateAsync(HuiaAuthenticationDefaults.ApplicationScheme);

        return signedIn.Succeeded
            ? Redirect(await ReturnUrlValidator.ResolveAsync(Request, ReturnUrl, applicationManager))
            : Page();
    }

    /// <summary>Creates the account and either sends a confirmation email or signs the user in immediately.</summary>
    // One line over MA0051's default 60-line limit; splitting this single linear registration flow wouldn't
    // reduce its actual complexity, just spread it across an extra method.
#pragma warning disable MA0051
    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        if (!options.RegistrationEnabled)
        {
            return NotFound();
        }

        ReturnUrl = returnUrl ?? Url.Content("~/");

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = new HuiaUser
            { UserName = Input.Email, Email = Input.Email, FirstName = Input.FirstName, LastName = Input.LastName };
        var result = await userManager.CreateAsync(user, Input.Password);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return Page();
        }

        logger.LogInformation("User created a new account.");

        var userId = await userManager.GetUserIdAsync(user);
        await events.PublishAsync(new UserRegisteredEvent<string>(userId));

        var code = await userManager.GenerateEmailConfirmationTokenAsync(user);
        code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));

        // When RequireConfirmedAccount is false, the sign-in below consumes ReturnUrl synchronously in this
        // same request — including, when it came from /connect/authorize, exchanging the client's one-time
        // PKCE code_verifier cookie. That cookie is gone once the client's OAuth round trip completes, so if
        // the confirmation email baked the same returnUrl in and the user later clicked it, replaying that
        // authorize URL would fail the PKCE check on the client (e.g. Auth.js's "pkceCodeVerifier value could
        // not be parsed", surfaced to the user as a generic server error) rather than sending them anywhere
        // useful. Resolving to the client's HomeUri/origin instead — see ClientHomeResolver — means the
        // "Sign in" link on ConfirmEmail sends them back to the actual application they registered from
        // (where its own "Sign in" starts a fresh flow) instead of Huia's own bare root.
        var confirmationReturnUrl = userManager.Options.SignIn.RequireConfirmedAccount
            ? ReturnUrl
            : await ApplicationHomeUriResolver.ResolveFromAuthorizeReturnUrlAsync(ReturnUrl, applicationManager);

        var confirmationLink = Url.PageLink("./ConfirmEmail",
                                   values: new { userId, code, returnUrl = confirmationReturnUrl },
                                   protocol: Request.Scheme)
                               ?? throw new InvalidOperationException(
                                   "The email confirmation link could not be generated.");

        await emailSender.SendConfirmationLinkAsync(user, Input.Email, confirmationLink);

        if (userManager.Options.SignIn.RequireConfirmedAccount)
        {
            return RedirectToPage("RegisterConfirmation", new { email = Input.Email, returnUrl = ReturnUrl });
        }

        await signInManager.SignInAsync(user, isPersistent: false);

        await events.PublishAsync(new UserSignedInEvent<string>(userId));

        return Redirect(await ReturnUrlValidator.ResolveAsync(Request, ReturnUrl, applicationManager));
    }
#pragma warning restore MA0051

    /// <summary>The registration form fields.</summary>
    public sealed class InputModel
    {
        /// <summary>The new account's given (first) name.</summary>
        [Required(ErrorMessage = "First name is required.")]
        [PersonName(ErrorMessage = "First name may only contain letters, spaces, hyphens, and apostrophes, and be at most 256 characters.")]
        [Display(Name = "First name")]
        public string FirstName { get; set; } = string.Empty;

        /// <summary>The new account's family (last) name.</summary>
        [Required(ErrorMessage = "Last name is required.")]
        [PersonName(ErrorMessage = "Last name may only contain letters, spaces, hyphens, and apostrophes, and be at most 256 characters.")]
        [Display(Name = "Last name")]
        public string LastName { get; set; } = string.Empty;

        /// <summary>The new account's email address (also used as the username).</summary>
        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        public string Email { get; set; } = string.Empty;

        /// <summary>The new account's password.</summary>
        [Required(ErrorMessage = "Password is required.")]
        [StringLength(100, ErrorMessage = "{0} must be at least {2} characters long.", MinimumLength = 6)]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        /// <summary>Must match <see cref="Password"/>.</summary>
        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare(nameof(Password), ErrorMessage = "The password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}