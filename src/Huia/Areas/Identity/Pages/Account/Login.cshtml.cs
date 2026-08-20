using System.ComponentModel.DataAnnotations;
using Huia.Eventing;
using Huia.Common;
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
/// Signs a user in with whichever 1FA method(s) are enabled (see <see cref="HuiaOptions.Authentication"/>) —
/// email/password itself, via <see cref="OnPostAsync"/>; passwordless phone/OTP posts to
/// <c>PhoneLoginModel</c> instead. Renders both as Basecoat UI tabs when both are enabled (see
/// docs/passwordless.md's hybrid-auth security considerations). Publishes <see cref="UserSignedInEvent{TKey}"/>
/// on a successful email/password sign-in.
/// </summary>
[AllowAnonymous]
public class LoginModel(
    SignInManager<HuiaUser> signInManager,
    UserManager<HuiaUser> userManager,
    IEventPublisher events,
    IOpenIddictApplicationManager applicationManager,
    HuiaOptions options,
    IStringLocalizer<HuiaResources> localizer,
    ILogger<LoginModel> logger) : PageModel
{
    /// <summary>The submitted form data.</summary>
    [BindProperty]
    public InputModel Input { get; set; } = new();

    /// <summary>Where to redirect after a successful sign-in.</summary>
    public string ReturnUrl { get; set; } = "~/";

    /// <summary>Registered external (third-party) sign-in providers, for rendering one button each.</summary>
    public IList<AuthenticationScheme> ExternalLogins { get; set; } = [];

    /// <summary>Whether <c>huia.Authentication.UseEmailAndPasswordFlow()</c> is enabled.</summary>
    public bool ShowEmailPasswordTab => options.Authentication.EmailAndPasswordFlowEnabled;

    /// <summary>Whether <c>huia.Authentication.UsePasswordlessFlow()</c> is enabled.</summary>
    public bool ShowPasswordlessTab => options.Authentication.PasswordlessFlowEnabled;

    /// <summary>Whether the "Create account" link should render — email/password sign-in is enabled and
    /// <c>huia.DisableRegistration()</c> wasn't called.</summary>
    public bool ShowRegisterLink => ShowEmailPasswordTab && options.RegistrationEnabled;

    /// <summary>Countries offered by the phone form's country selector (see <c>_PhoneLoginForm.cshtml</c>),
    /// each paired with its calling code and a localized display name. Left empty when
    /// <see cref="ShowPasswordlessTab"/> is false, since building it is pure overhead otherwise.</summary>
    public IReadOnlyList<CountryPhoneCode> CountryOptions { get; private set; } = [];

    /// <summary>ISO 3166-1 alpha-2 code the phone form's country selector preselects, from
    /// <c>huia.Authentication.UsePasswordlessFlow(passwordless => passwordless.DefaultCountryCode = ...)</c> —
    /// <see langword="null"/> for no default.</summary>
    public string? DefaultCountryCode { get; private set; }

    /// <summary>The Cloudflare Turnstile site key the phone form's widget renders with, from
    /// <c>huia.Authentication.UsePasswordlessFlow(passwordless => passwordless.UseTurnstile(...))</c> —
    /// <see langword="null"/> when Turnstile isn't configured, in which case the form renders no widget at
    /// all.</summary>
    public string? TurnstileSiteKey { get; private set; }

    /// <summary>Set by <c>ExternalLoginModel</c>/<c>ExternalLoginConfirmationModel</c> when an external
    /// sign-in couldn't complete, carried across the redirect back to this page.</summary>
    [TempData]
    public string? ExternalLoginError { get; set; }

    /// <summary>Set by <c>PhoneLoginModel</c> when a phone number has hit its OTP request cooldown, carried
    /// across the redirect back to this page.</summary>
    [TempData]
    public string? PhoneLoginError { get; set; }

    /// <summary>Whether the hybrid tab switcher (see Login.cshtml) should default to the Phone tab instead
    /// of Email — true whenever this page was reached because of <see cref="PhoneLoginError"/>, so the
    /// cooldown message shows up next to the form the user was actually using.</summary>
    public bool PreferPhoneTab => PhoneLoginError is not null;

    /// <summary>
    /// Renders the page, or — if the user is already signed in (e.g. another browser tab just completed a
    /// sign-in that this one's cross-tab sync script noticed, see <c>huia-session-sync.js</c>) — redirects
    /// straight to <paramref name="returnUrl"/> instead of showing a stale login form.
    /// </summary>
    public async Task<IActionResult> OnGetAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl ?? Url.Content("~/");
        ExternalLogins = (await signInManager.GetExternalAuthenticationSchemesAsync()).ToList();
        if (ShowPasswordlessTab)
        {
            CountryOptions = CountryPhoneCodeProvider.GetAll();
            DefaultCountryCode = options.Authentication.Passwordless.DefaultCountryCode;
            TurnstileSiteKey = options.Authentication.Passwordless.Turnstile?.SiteKey;
        }

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
        ExternalLogins = (await signInManager.GetExternalAuthenticationSchemesAsync()).ToList();
        if (ShowPasswordlessTab)
        {
            CountryOptions = CountryPhoneCodeProvider.GetAll();
            DefaultCountryCode = options.Authentication.Passwordless.DefaultCountryCode;
            TurnstileSiteKey = options.Authentication.Passwordless.Turnstile?.SiteKey;
        }

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
                await events.PublishAsync(new UserSignedInEvent<string>(user.Id));
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

        ModelState.AddModelError(string.Empty, localizer["InvalidCredentialsError"]);
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