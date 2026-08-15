using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Huia.Common;
using Huia.Identity;
using Huia.Localization;
using Huia.Passwordless;
using Huia.Sms;
using Huia.Stores;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace Huia.Areas.Identity.Pages.Account;

/// <summary>
/// Starts passwordless phone sign-in: validates and rate-limits the submitted phone number, looks up or
/// creates the account, sends an OTP, and hands off to <see cref="PhoneLoginVerifyModel"/> via the
/// <c>Huia.PhoneVerification</c> cookie. Registered via <c>huia.Authentication.UsePasswordlessFlow()</c>.
/// Not meant to be navigated to directly; reached by posting from the phone form on <see cref="LoginModel"/>'s
/// page.
/// </summary>
[AllowAnonymous]
public class PhoneLoginModel(
    UserManager<HuiaUser> userManager,
    IHuiaPhoneNumberStore phoneNumberStore,
    IPhoneOtpRateLimiter rateLimiter,
    ISmsSender<HuiaUser> smsSender,
    IStringLocalizer<HuiaResources> localizer,
    ILogger<PhoneLoginModel> logger) : PageModel
{
    /// <summary>The submitted form data.</summary>
    [BindProperty]
    public InputModel Input { get; set; } = new();

    /// <summary>Redirects here directly (no phone number submitted) back to <c>Login</c>.</summary>
    public IActionResult OnGet() => RedirectToPage("./Login");

    /// <summary>
    /// Validates and rate-limits the phone number, looks up or creates the account, sends an OTP, and
    /// redirects to <see cref="PhoneLoginVerifyModel"/>. A rate-limited request is sent back to
    /// <see cref="LoginModel"/> with an explicit cooldown message instead — this only ever reveals how often
    /// <em>this specific number</em> has been requesting codes, never whether it belongs to an account (the
    /// limiter throttles a never-registered number exactly the same as a registered one), so it doesn't
    /// reopen the new-registration/returning-user/collision enumeration resistance described in
    /// docs/passwordless.md's hybrid-auth security considerations — that part still shows the identical
    /// redirect regardless of which of those three cases occurred.
    /// </summary>
    // One line over MA0051's default 60-line limit; splitting this single linear request flow wouldn't
    // reduce its actual complexity, just spread it across an extra method.
#pragma warning disable MA0051
    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        returnUrl ??= Url.Content("~/");

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var normalized = PhoneNumberValidator.TryNormalize(Input.PhoneNumber);
        if (normalized is null)
        {
            ModelState.AddModelError(nameof(Input.PhoneNumber), localizer["InvalidPhoneNumberError"]);
            return Page();
        }

        var acquireResult = await rateLimiter.TryAcquireAsync(normalized, HttpContext.RequestAborted);
        if (!acquireResult.IsAcquired)
        {
            // PhoneLogin.cshtml itself renders nothing (see its own doc comment) — the message and the
            // phone form it applies to both live on LoginModel's page, so this hands the cooldown across via
            // TempData (the same pattern ExternalLoginModel uses for its own error) and round-trips
            // returnUrl through the redirect so it isn't lost.
            TempData["PhoneLoginError"] = PhoneOtpCooldownFormatter.FormatMessage(localizer, acquireResult.RetryAfter);
            return RedirectToPage("./Login", new { returnUrl });
        }

        var user = await phoneNumberStore.FindByNormalizedPhoneNumberAsync(normalized, HttpContext.RequestAborted);
        if (user is null || !user.PasswordlessLoginEnabled)
        {
            // Either a brand-new number, or one that already belongs to a non-passwordless account (e.g.
            // recorded for a future SMS-2FA feature) — either way, a new, distinct account is created here
            // rather than reusing an existing one: OTP possession is a weaker proof than whatever originally
            // protects that other account. See docs/passwordless.md's hybrid-auth security considerations.
            var newUser = new HuiaUser
            {
                UserName = normalized,
                PhoneNumber = normalized,
                NormalizedPhoneNumber = normalized,
                PasswordlessLoginEnabled = true,
            };

            var createResult = await userManager.CreateAsync(newUser);
            if (createResult.Succeeded)
            {
                user = newUser;
            }
            else if (createResult.Errors.Any(e => string.Equals(e.Code,
                         nameof(Microsoft.AspNetCore.Identity.IdentityErrorDescriber.DuplicateUserName),
                         StringComparison.Ordinal)))
            {
                // Raced with a concurrent request for the same number — reuse the row it created.
                user = await phoneNumberStore.FindByNormalizedPhoneNumberAsync(normalized, HttpContext.RequestAborted);
            }
            else
            {
                foreach (var error in createResult.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }

                return Page();
            }
        }

        if (user is null)
        {
            ModelState.AddModelError(string.Empty, localizer["GenericRetryError"]);
            return Page();
        }

        var code = await userManager.GenerateTwoFactorTokenAsync(user, TokenOptions.DefaultPhoneProvider);
        await smsSender.SendOtpAsync(user, normalized, code);

        logger.LogInformation("OTP requested for {MaskedPhoneNumber}.", PhoneNumberMasker.Mask(normalized));

        Claim[] claims =
        [
            new Claim(PhoneVerificationScheme.PhoneNumberClaimType, normalized),
            new Claim(PhoneVerificationScheme.UserIdClaimType, user.Id),
        ];
        var identity = new ClaimsIdentity(claims, PhoneVerificationScheme.Name);
        await HttpContext.SignInAsync(PhoneVerificationScheme.Name, new ClaimsPrincipal(identity));

        return RedirectToPage("./PhoneLoginVerify", new { returnUrl });
    }
#pragma warning restore MA0051

    /// <summary>The phone-entry form fields.</summary>
    public sealed class InputModel
    {
        /// <summary>The phone number to sign in with, in whatever format the client submits.</summary>
        [Required(ErrorMessage = "Phone number is required.")]
        public string PhoneNumber { get; set; } = string.Empty;
    }
}
