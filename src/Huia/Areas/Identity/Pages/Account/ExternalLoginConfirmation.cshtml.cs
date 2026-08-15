using System.ComponentModel.DataAnnotations;
using System.Text;
using Huia.Common;
using Huia.Eventing;
using Huia.Identity;
using Huia.Localization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;

namespace Huia.Areas.Identity.Pages.Account;

/// <summary>
/// Explicit-consent account creation for a first-time external sign-in — reached from
/// <see cref="ExternalLoginModel.OnGetCallbackAsync"/> when the provider's identity isn't linked to any local
/// account yet <em>and</em> auto-provisioning there didn't apply: the provider left out a claim (no verified
/// email, or no given/family name — common for a generic <c>AddOAuth</c> registration) or account creation
/// itself failed. Creates the account (no password) and links the external login, publishing
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
    IStringLocalizer<HuiaResources> localizer,
    ILogger<ExternalLoginConfirmationModel> logger) : PageModel
{
    /// <summary>The submitted form data.</summary>
    [BindProperty]
    public InputModel Input { get; set; } = new();

    /// <summary>Where to redirect after the account is created.</summary>
    public string ReturnUrl { get; set; } = "~/";

    /// <summary>The provider the pending external identity came from, for display.</summary>
    public string ProviderDisplayName { get; set; } = string.Empty;

    /// <summary>Whether <see cref="InputModel.Email"/> came from the provider and should be rendered
    /// read-only — a generic provider isn't guaranteed to supply it (see <see cref="ExternalClaimsMapper"/>),
    /// in which case the user still has to type one in.</summary>
    public bool EmailFromProvider { get; set; }

    /// <summary>Whether <see cref="InputModel.FirstName"/> came from the provider and should be rendered
    /// read-only. See <see cref="EmailFromProvider"/>.</summary>
    public bool FirstNameFromProvider { get; set; }

    /// <summary>Whether <see cref="InputModel.LastName"/> came from the provider and should be rendered
    /// read-only. See <see cref="EmailFromProvider"/>.</summary>
    public bool LastNameFromProvider { get; set; }

    /// <summary>Renders the page, pre-filled from the pending external identity's claims.</summary>
    public async Task<IActionResult> OnGetAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl ?? Url.Content("~/");

        var info = await signInManager.GetExternalLoginInfoAsync();
        if (info is null)
        {
            TempData["ExternalLoginError"] = (string)localizer["ExternalLoginConfirmationInfoMissingError"];
            return RedirectToPage("./Login", new { ReturnUrl });
        }

        ProviderDisplayName = info.ProviderDisplayName ?? info.LoginProvider;
        ApplyProviderSuppliedFields(info);

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
            TempData["ExternalLoginError"] = (string)localizer["ExternalLoginConfirmationInfoMissingError"];
            return RedirectToPage("./Login", new { ReturnUrl });
        }

        ProviderDisplayName = info.ProviderDisplayName ?? info.LoginProvider;

        // Fields the provider already supplied are never taken from the posted form — a disabled input
        // doesn't stop a tampered request from including one anyway, so this re-derives them from the
        // still-live external identity's own claims (same as the GET above) and drops whatever validation
        // the posted value triggered, rather than trusting the client not to have changed them.
        ApplyProviderSuppliedFields(info);

        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (await userManager.FindByEmailAsync(Input.Email) is not null)
        {
            ModelState.AddModelError(string.Empty,
                localizer["ExternalLoginEmailCollisionError", Input.Email, ProviderDisplayName]);
            return Page();
        }

        var emailVerifiedByProvider = ExternalClaimsMapper.IsEmailVerified(info.Principal);
        var (user, errors) = await ExternalAccountFactory.CreateAsync(userManager, info, Input.Email,
            Input.FirstName, Input.LastName, emailConfirmed: emailVerifiedByProvider);
        if (user is null)
        {
            foreach (var error in errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            return Page();
        }

        logger.LogInformation("User created a new account via {LoginProvider}.", info.LoginProvider);

        var userId = await userManager.GetUserIdAsync(user);
        await events.PublishAsync(new UserRegisteredEvent<string>(userId, Input.Email));

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
    /// Fills <see cref="Input"/> from whatever profile claims <paramref name="info"/> carries, marking each
    /// field the provider actually supplied so the page can render it read-only — and, on a POST, discarding
    /// any validation error binding recorded against it, since its value came from here rather than the
    /// posted form. A field the provider didn't supply (e.g. a generic OAuth provider with no email claim)
    /// is left alone for the user to fill in.
    /// </summary>
    private void ApplyProviderSuppliedFields(ExternalLoginInfo info)
    {
        var email = ExternalClaimsMapper.GetEmail(info.Principal);
        EmailFromProvider = !string.IsNullOrEmpty(email);
        if (EmailFromProvider)
        {
            Input.Email = email!;
            RemoveModelState(nameof(Input.Email));
        }

        var firstName = ExternalClaimsMapper.GetFirstName(info.Principal);
        FirstNameFromProvider = !string.IsNullOrEmpty(firstName);
        if (FirstNameFromProvider)
        {
            Input.FirstName = firstName!;
            RemoveModelState(nameof(Input.FirstName));
        }

        var lastName = ExternalClaimsMapper.GetLastName(info.Principal);
        LastNameFromProvider = !string.IsNullOrEmpty(lastName);
        if (LastNameFromProvider)
        {
            Input.LastName = lastName!;
            RemoveModelState(nameof(Input.LastName));
        }
    }

    /// <summary>
    /// Clears any binding/validation error recorded for a disabled provider-supplied field under both keys
    /// it could end up bound to: the normal prefixed key ("Input.Email") a partially-submitted form produces,
    /// and the bare property name ("Email") ASP.NET Core's model binder falls back to when a disabled input
    /// means the whole "Input.*" prefix has zero matches in the posted form — which happens here whenever
    /// every field on the page is provider-supplied (all three disabled), the common case for a
    /// Google/Microsoft-style provider. Removing only the prefixed key left the bare-key error in
    /// <see cref="ModelState"/> in that case, so <c>ModelState.IsValid</c> stayed false and this page kept
    /// silently re-rendering itself instead of creating the account — invisibly, since neither
    /// <c>asp-validation-for="Input.Email"</c> (a different key) nor <c>asp-validation-summary="ModelOnly"</c>
    /// (a model-level, not field-level, key) ever displays an error stored under the bare key.
    /// </summary>
    private void RemoveModelState(string propertyName)
    {
        ModelState.Remove($"{nameof(Input)}.{propertyName}");
        ModelState.Remove(propertyName);
    }

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
