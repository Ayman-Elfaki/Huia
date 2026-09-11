using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using Huia.OpenId.Flows;
using Huia.Services;
using Huia.OpenId.UI;
using Huia.Events;
using Finbuckle.MultiTenant.Abstractions;
using Huia.OpenId.Identity;
using Huia.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace Huia.OpenId.Areas.Identity.Pages.Account;

/// <summary>
/// The single sign-in page. The default handler is interactive username/password; the <c>Phone</c>
/// handler (the phone tab) collects a number and dispatches a one-time code, then hands off to
/// <c>VerifyOtp</c>.
/// </summary>
public sealed class LoginModel(
    IHuiaFlowIdentityFactory flowIdentity,
    IReturnUrlProtector returnUrlProtector,
    IMultiTenantContextAccessor tenantAccessor,
    IHuiaEventPublisher events,
    ICountryCatalog countryCatalog,
    IPhoneNumberService phoneNumbers,
    IOtpService otpService,
    IPendingPhoneSignup pendingSignups,
    IOtpRateLimiter rateLimiter,
    IPhoneLoginRateLimiter phoneLoginRateLimiter,
    ISmsSender smsSender,
    ICaptchaVerifier captcha,
    IStringLocalizer<SharedResource> localizer,
    TimeProvider timeProvider) : HuiaAccountPageModel
{
    /// <summary>The bound credentials (email/password) and phone fields.</summary>
    [BindProperty]
    public InputModel Input { get; set; } = new();

    /// <summary>The countries for the phone-tab country picker, when phone login is enabled.</summary>
    public IReadOnlyList<CountryDialInfo> Countries => countryCatalog.GetCountries();

    /// <summary>The tenant's configured default region, used to preselect the phone-tab country picker.</summary>
    public string? PhoneDefaultCountry => Tenant?.Authentication.Phone?.DefaultCountry;

    /// <summary>The sanitized return URL carried through the form.</summary>
    public string ReturnUrl { get; private set; } = "/";

    private PhoneOptions PhoneOptions =>
        Tenant?.Authentication.Phone ?? throw new InvalidOperationException("Phone login is not enabled.");

    /// <summary>Handles the initial GET.</summary>
    /// <param name="returnUrl">The URL to return to after signing in.</param>
    /// <param name="linkError">Set by the external callback when an email already belongs to an account.</param>
    public void OnGet(string? returnUrl, bool linkError = false)
    {
        SetHeadings();
        ReturnUrl = returnUrlProtector.SanitizeReturnUrl(returnUrl, HttpContext);
        if (linkError)
        {
            ErrorMessage = localizer["Login.ExternalEmailTaken"].Value;
        }
    }

    /// <summary>Handles the credential (email/password) POST.</summary>
    /// <param name="returnUrl">The URL to return to after signing in.</param>
    /// <returns>A redirect to <paramref name="returnUrl"/> on success, otherwise the page with an error.</returns>
    public async Task<IActionResult> OnPostAsync(string? returnUrl)
    {
        SetHeadings();
        ReturnUrl = returnUrlProtector.SanitizeReturnUrl(returnUrl, HttpContext);

        if (!IsEmailAndPasswordLoginEnabled)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(Input.Email) || string.IsNullOrWhiteSpace(Input.Password))
        {
            ErrorMessage = localizer["Login.Failed"].Value;
            return Page();
        }

        var password = flowIdentity.Create(HuiaAuthFlow.EmailAndPasswordLogin);
        var user = await password.UserManager.FindByEmailAsync(Input.Email)
                   ?? await password.UserManager.FindByNameAsync(Input.Email);

        if (user is not null)
        {
            var result = await password.SignInManager.PasswordSignInAsync(user, Input.Password, Input.RememberMe, lockoutOnFailure: true);
            if (result.Succeeded)
            {
                await events.PublishAsync(new UserLoggedInEvent(
                    tenantAccessor.RequireCurrentTenantId(), user.Id, HuiaConstants.AuthenticationMethods.Password, null, timeProvider.GetUtcNow()));
                return await ResolvePostSignUpRedirectAsync(user, ReturnUrl);
            }

            if (result.IsLockedOut)
            {
                ErrorMessage = localizer["Login.LockedOut"].Value;
                return Page();
            }
        }

        ErrorMessage = localizer["Login.Failed"].Value;
        return Page();
    }

    /// <summary>Handles the phone-tab POST: validates a number, throttles, sends a code, moves to verification.</summary>
    /// <param name="returnUrl">The URL to return to after signing in.</param>
    /// <returns>A redirect to the verify page, or the page with an error.</returns>
    public async Task<IActionResult> OnPostPhoneAsync(string? returnUrl)
    {
        SetHeadings();
        ReturnUrl = returnUrlProtector.SanitizeReturnUrl(returnUrl, HttpContext);

        if (!IsPhoneLoginEnabled)
        {
            return NotFound();
        }

        var options = PhoneOptions;
        var tenantId = tenantAccessor.RequireCurrentTenantId();

        if (options.Captcha == CaptchaMode.Always && !await captcha.VerifyAsync(Request.Form["captcha"], HttpContext.RequestAborted))
        {
            ErrorMessage = localizer["PhoneLogin.CaptchaRequired"].Value;
            return Page();
        }

        var defaultCountry = Input.Country ?? Tenant?.Authentication.Phone?.DefaultCountry;

        if (!phoneNumbers.TryNormalize(Input.PhoneNumber, defaultCountry, out var e164))
        {
            ErrorMessage = localizer["PhoneLogin.Invalid"].Value;
            return Page();
        }

        if (!rateLimiter.TryAcquire(tenantId, e164))
        {
            ErrorMessage = localizer["PhoneLogin.RateLimited"].Value;
            return Page();
        }

        // The successful-sign-in ceiling is checked here, before an SMS is spent — the actual permit is
        // consumed later, once verification succeeds (VerifyOtp).
        if (!phoneLoginRateLimiter.CanRecordLogin(tenantId, e164, out var retryAfter, out var dailyLimitReached))
        {
            ErrorMessage = dailyLimitReached
                ? localizer["VerifyOtp.RateLimitedDaily"].Value
                : string.Format(
                    CultureInfo.CurrentCulture,
                    localizer["VerifyOtp.RateLimited"].Value,
                    RetryAfterText.Format(localizer, retryAfter ?? options.SuccessfulLoginWindow));
            return Page();
        }

        var user = await flowIdentity.Create(HuiaAuthFlow.PhoneLogin).UserManager.FindByPhoneNumberAsync(e164);

        var state = new AuthFlowState { ReturnUrl = ReturnUrl, PhoneNumber = e164 };
        var maskedForEvent = phoneNumbers.Mask(e164);
        bool delivered;

        if (user is not null)
        {
            var code = await otpService.IssueAsync(user, options);
            delivered = await smsSender.SendOtpAsync(tenantId, e164, code, HttpContext.RequestAborted);
            state = state with { UserId = user.Id };
            await events.PublishAsync(new OtpRequestedEvent(tenantId, user.Id, maskedForEvent, delivered, timeProvider.GetUtcNow()));
        }
        else if (options.AllowAutoProvisioning)
        {
            var code = otpService.GenerateCode(options);
            var pendingId = pendingSignups.Create(tenantId, e164, code, options);
            delivered = await smsSender.SendOtpAsync(tenantId, e164, code, HttpContext.RequestAborted);
            state = state with { PendingSignupId = pendingId };
            await events.PublishAsync(new OtpRequestedEvent(tenantId, null, maskedForEvent, delivered, timeProvider.GetUtcNow()));
        }
        else
        {
            // Unknown number, auto-provisioning off: behave identically to avoid enumeration.
            await events.PublishAsync(new OtpRequestedEvent(tenantId, null, maskedForEvent, false, timeProvider.GetUtcNow()));
        }

        return RedirectToPage("./VerifyOtp", new { flow = returnUrlProtector.Tokenize(state) });
    }

    private void SetHeadings()
    {
        ViewData["Title"] = localizer["Login.Title"].Value;
        ViewData["Heading"] = localizer["Login.Heading"].Value;
    }

    /// <summary>The sign-in form fields (email/password on the default tab, number on the phone tab).</summary>
    public sealed class InputModel
    {
        /// <summary>Email address or username.</summary>
        public string Email { get; set; } = string.Empty;

        /// <summary>The password.</summary>
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        /// <summary>Whether to issue a persistent cookie.</summary>
        public bool RememberMe { get; set; }

        /// <summary>The phone number as entered (E.164 or national), for the phone tab.</summary>
        public string PhoneNumber { get; set; } = string.Empty;

        /// <summary>The selected ISO country, used to interpret a national number.</summary>
        public string? Country { get; set; }
    }
}
