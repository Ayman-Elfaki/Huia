using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Huia.Common;
using Huia.Eventing;
using Huia.Identity;
using Huia.Localization;
using Huia.Passwordless;
using Huia.Sms;
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
/// Verifies the OTP a <see cref="PhoneLoginModel"/> request sent, using the <c>Huia.PhoneVerification</c>
/// cookie (never a posted/route value) to know which phone number and account it's for — see
/// docs/passwordless.md. A brand-new account (this is its first-ever successful verification, detected via
/// <c>PhoneNumberConfirmed</c> not already being set) is routed to <see cref="PhoneLoginConfirmationModel"/>
/// to collect a name before completing sign-in; a returning passwordless user signs in immediately.
/// </summary>
[AllowAnonymous]
public class PhoneLoginVerifyModel(
    UserManager<HuiaUser> userManager,
    SignInManager<HuiaUser> signInManager,
    IPhoneOtpRateLimiter rateLimiter,
    ISmsSender<HuiaUser> smsSender,
    IEventPublisher events,
    IOpenIddictApplicationManager applicationManager,
    IStringLocalizer<HuiaResources> localizer,
    ILogger<PhoneLoginVerifyModel> logger) : PageModel
{
    /// <summary>How long after a code is (or appears to be) sent before Resend may be used again — kept in
    /// lockstep with <see cref="Huia.Passwordless.PhoneOtpRateLimitOptions.RequestsPerMinute"/>'s window
    /// (always one rolling minute; only the permit count within it is configurable), so a resend clicked
    /// once this elapses always actually reaches <see cref="IPhoneOtpRateLimiter"/> rather than silently
    /// colliding with the same-number-same-minute cap <see cref="PhoneLoginModel"/>'s own send already
    /// consumed. See docs/passwordless.md.
    /// </summary>
    private static readonly TimeSpan ResendCooldown = TimeSpan.FromMinutes(1);

    /// <summary>The submitted form data.</summary>
    [BindProperty]
    public InputModel Input { get; set; } = new();

    /// <summary>Where to redirect after a successful sign-in.</summary>
    public string ReturnUrl { get; set; } = "~/";

    /// <summary>The phone number an OTP was sent to, masked, for display only.</summary>
    public string MaskedPhoneNumber { get; set; } = string.Empty;

    /// <summary>Seconds remaining before Resend may be used again — 0 once it can. Computed from when the
    /// current pending code was actually (or, if rate-limited, apparently) issued, so it stays correct
    /// across page reloads and back-navigation rather than resetting to a fixed value on every render.
    /// </summary>
    public int SecondsUntilResendAllowed { get; set; }

    /// <summary>Renders the page, or back to <c>PhoneLogin</c> if no verification is actually pending.</summary>
    public async Task<IActionResult> OnGetAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl ?? Url.Content("~/");

        var pending = await AuthenticatePendingAsync();
        if (pending is null)
        {
            return RedirectToPage("./PhoneLogin");
        }

        MaskedPhoneNumber = PhoneNumberMasker.Mask(pending.Value.PhoneNumber);
        SecondsUntilResendAllowed = SecondsRemaining(pending.Value.IssuedAt);
        return Page();
    }

    /// <summary>
    /// Resends the OTP for the still-pending phone number, exactly like <see cref="PhoneLoginModel"/>'s own
    /// send — same rate limiter. Unlike the initial send, a denied resend doesn't need to stay opaque: it's
    /// only reachable with a live pending cookie already proving this phone number's flow was legitimately
    /// started, not a blind probe of an arbitrary number, so showing the real cooldown here doesn't reopen
    /// any account-existence enumeration (see docs/passwordless.md). The original code is untouched either
    /// way — a denied resend leaves the still-pending cookie (and the code already sent for it) exactly as
    /// it was, so it's still enterable.
    /// </summary>
    public async Task<IActionResult> OnPostResendAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl ?? Url.Content("~/");

        var pending = await AuthenticatePendingAsync();
        if (pending is null)
        {
            return RedirectToPage("./PhoneLogin");
        }

        MaskedPhoneNumber = PhoneNumberMasker.Mask(pending.Value.PhoneNumber);

        var acquireResult = await rateLimiter.TryAcquireAsync(pending.Value.PhoneNumber, HttpContext.RequestAborted);
        if (!acquireResult.IsAcquired)
        {
            SecondsUntilResendAllowed = Math.Max(SecondsRemaining(pending.Value.IssuedAt),
                (int)Math.Ceiling(acquireResult.RetryAfter.TotalSeconds));
            ModelState.AddModelError(string.Empty, PhoneOtpCooldownFormatter.FormatMessage(localizer, acquireResult.RetryAfter));
            return Page();
        }

        var user = await userManager.FindByIdAsync(pending.Value.UserId);
        if (user is null)
        {
            await HttpContext.SignOutAsync(PhoneVerificationScheme.Name);
            return RedirectToPage("./PhoneLogin");
        }

        var code = await userManager.GenerateTwoFactorTokenAsync(user, TokenOptions.DefaultPhoneProvider);
        await smsSender.SendOtpAsync(user, pending.Value.PhoneNumber, code);

        logger.LogInformation("OTP resent for {MaskedPhoneNumber}.", MaskedPhoneNumber);

        Claim[] claims =
        [
            new Claim(PhoneVerificationScheme.PhoneNumberClaimType, pending.Value.PhoneNumber),
            new Claim(PhoneVerificationScheme.UserIdClaimType, pending.Value.UserId),
        ];
        var identity = new ClaimsIdentity(claims, PhoneVerificationScheme.Name);
        await HttpContext.SignInAsync(PhoneVerificationScheme.Name, new ClaimsPrincipal(identity));

        return RedirectToPage("./PhoneLoginVerify", new { returnUrl = ReturnUrl });
    }

    /// <summary>Verifies the submitted code and either completes sign-in or routes to the confirmation page.</summary>
    // One line over MA0051's default 60-line limit; splitting this single linear verification flow wouldn't
    // reduce its actual complexity, just spread it across an extra method.
#pragma warning disable MA0051
    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl ?? Url.Content("~/");

        var pending = await AuthenticatePendingAsync();
        if (pending is null)
        {
            return RedirectToPage("./PhoneLogin");
        }

        MaskedPhoneNumber = PhoneNumberMasker.Mask(pending.Value.PhoneNumber);
        SecondsUntilResendAllowed = SecondsRemaining(pending.Value.IssuedAt);

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await userManager.FindByIdAsync(pending.Value.UserId);
        if (user is null)
        {
            await HttpContext.SignOutAsync(PhoneVerificationScheme.Name);
            return RedirectToPage("./PhoneLogin");
        }

        var code = Input.Code.Replace(" ", string.Empty).Replace("-", string.Empty);
        var valid = await userManager.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultPhoneProvider, code);

        if (!valid)
        {
            await userManager.AccessFailedAsync(user);
            if (await userManager.IsLockedOutAsync(user))
            {
                await HttpContext.SignOutAsync(PhoneVerificationScheme.Name);
                return RedirectToPage("./Lockout");
            }

            ModelState.AddModelError(string.Empty, localizer["InvalidOrExpiredCodeError"]);
            return Page();
        }

        await userManager.ResetAccessFailedCountAsync(user);

        // Not yet confirmed means this is the first time an OTP has ever been successfully verified for this
        // account — i.e. it was just created by PhoneLoginModel and still needs a name. A returning
        // passwordless user's PhoneNumberConfirmed is already true from their first verification.
        var isFirstVerification = !user.PhoneNumberConfirmed;
        user.PhoneNumberConfirmed = true;
        await userManager.UpdateAsync(user);

        if (isFirstVerification)
        {
            logger.LogInformation("Phone number verified for {MaskedPhoneNumber}; routing to confirmation.",
                MaskedPhoneNumber);

            await HttpContext.SignOutAsync(PhoneVerificationScheme.Name);

            Claim[] claims =
            [
                new Claim(PhoneVerificationScheme.UserIdClaimType, user.Id),
                new Claim(PhoneVerificationScheme.VerifiedClaimType, "true"),
            ];
            var identity = new ClaimsIdentity(claims, PhoneVerificationScheme.Name);
            await HttpContext.SignInAsync(PhoneVerificationScheme.Name, new ClaimsPrincipal(identity));

            return RedirectToPage("./PhoneLoginConfirmation", new { ReturnUrl });
        }

        logger.LogInformation("User signed in via passwordless phone.");
        await signInManager.SignInAsync(user, isPersistent: false);
        await events.PublishAsync(new UserSignedInEvent<string>(user.Id, user.Email));
        await HttpContext.SignOutAsync(PhoneVerificationScheme.Name);

        return Redirect(await ReturnUrlValidator.ResolveAsync(Request, ReturnUrl, applicationManager));
    }
#pragma warning restore MA0051

    private async Task<(string UserId, string PhoneNumber, DateTimeOffset? IssuedAt)?> AuthenticatePendingAsync()
    {
        var result = await HttpContext.AuthenticateAsync(PhoneVerificationScheme.Name);
        if (!result.Succeeded || result.Principal is null)
        {
            return null;
        }

        var userId = result.Principal.FindFirstValue(PhoneVerificationScheme.UserIdClaimType);
        var phoneNumber = result.Principal.FindFirstValue(PhoneVerificationScheme.PhoneNumberClaimType);

        return userId is null || phoneNumber is null ? null : (userId, phoneNumber, result.Properties.IssuedUtc);
    }

    private static int SecondsRemaining(DateTimeOffset? issuedAt)
    {
        if (issuedAt is null)
        {
            return 0;
        }

        var remaining = ResendCooldown - (DateTimeOffset.UtcNow - issuedAt.Value);
        return remaining > TimeSpan.Zero ? (int)Math.Ceiling(remaining.TotalSeconds) : 0;
    }

    /// <summary>The OTP verification form fields.</summary>
    public sealed class InputModel
    {
        /// <summary>The one-time code sent by SMS.</summary>
        [Required(ErrorMessage = "Code is required.")]
        public string Code { get; set; } = string.Empty;
    }
}
