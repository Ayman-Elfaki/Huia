using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Huia.AspNetCore.Flows;
using Huia.AspNetCore.Identity;
using Huia.AspNetCore.Services;
using Huia.AspNetCore.UI;
using Huia.EntityFrameworkCore.Entities;
using Huia.Events;
using Finbuckle.MultiTenant.Abstractions;
using Huia.EntityFrameworkCore.Multitenancy;
using Huia.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace Huia.AspNetCore.Areas.Identity.Pages.Account;

/// <summary>Step 2 of the passwordless SMS flow: verify the code and either sign in or complete the profile.</summary>
public sealed class VerifyOtpModel(
    IOtpService otpService,
    IPendingPhoneSignup pendingSignups,
    IOtpRateLimiter rateLimiter,
    IPhoneLoginRateLimiter phoneLoginRateLimiter,
    ISmsSender smsSender,
    IPhoneNumberService phoneNumbers,
    IHuiaFlowIdentityFactory flowIdentity,
    IMultiTenantContextAccessor tenantAccessor,
    IReturnUrlProtector returnUrlProtector,
    IHuiaEventPublisher events,
    IStringLocalizer<SharedResource> localizer,
    TimeProvider timeProvider) : HuiaAccountPageModel
{
    /// <summary>The bound code.</summary>
    [BindProperty]
    public InputModel Input { get; set; } = new();

    /// <summary>The opaque flow token round-tripped through the form.</summary>
    [BindProperty]
    public string Flow { get; set; } = string.Empty;

    /// <summary>Whether a fresh code was just re-sent.</summary>
    public bool Resent { get; private set; }

    /// <summary>The destination number, masked (for example <c>+1 ••• ••• 4589</c>), or <see langword="null"/>.</summary>
    public string? MaskedPhoneNumber { get; private set; }

    /// <summary>The expected one-time code length (4–10, default 6), for rendering a segmented input.</summary>
    public int CodeLength => Tenant?.Authentication.Phone?.CodeLength ?? 6;

    /// <summary>Seconds the "send a new code" button stays disabled between requests.</summary>
    public int ResendCooldownSeconds =>
        (int)Math.Ceiling((Tenant?.Authentication.Phone?.ResendCooldown ?? TimeSpan.FromSeconds(30)).TotalSeconds);

    private PhoneOptions PhoneOptions =>
        Tenant?.Authentication.Phone ?? throw new InvalidOperationException("Phone login is not enabled.");

    /// <summary>Handles the GET.</summary>
    /// <param name="flow">The flow token.</param>
    /// <param name="resent">Whether the previous action re-sent a code.</param>
    /// <returns>The page, or 404 when phone login is off or the token is missing.</returns>
    public IActionResult OnGet(string? flow, bool resent = false)
    {
        SetHeadings();
        Flow = flow ?? string.Empty;
        Resent = resent;

        var state = returnUrlProtector.Read(Flow);
        if (!IsPhoneLoginEnabled || state is null)
        {
            return NotFound();
        }

        MaskedPhoneNumber = state.PhoneNumber is { } number ? phoneNumbers.Mask(number) : null;
        return Page();
    }

    /// <summary>Handles the code POST.</summary>
    /// <returns>A redirect on success, otherwise the page with an error.</returns>
    public async Task<IActionResult> OnPostAsync()
    {
        SetHeadings();
        if (!IsPhoneLoginEnabled)
        {
            return NotFound();
        }

        var state = returnUrlProtector.Read(Flow);
        if (state is null)
        {
            return NotFound();
        }

        MaskedPhoneNumber = state.PhoneNumber is { } number ? phoneNumbers.Mask(number) : null;
        var options = PhoneOptions;
        var tenantId = tenantAccessor.RequireCurrentTenantId();
        var returnUrl = returnUrlProtector.SanitizeReturnUrl(state.ReturnUrl, HttpContext);

        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (state.PendingSignupId is { } pendingId)
        {
            var pendingResult = pendingSignups.Verify(pendingId, Input.Code, options);
            if (pendingResult != OtpVerifyResult.Success)
            {
                ErrorMessage = localizer["VerifyOtp.Invalid"].Value;
                return Page();
            }

            await events.PublishAsync(new OtpVerifiedEvent(tenantId, null, MaskFor(state), timeProvider.GetUtcNow()));
            return RedirectToPage("./CompleteProfile", new
            {
                flow = returnUrlProtector.Tokenize(new AuthFlowState { ReturnUrl = returnUrl, PhoneNumber = state.PhoneNumber, PendingSignupId = pendingId }),
            });
        }

        var phone = flowIdentity.Create(HuiaAuthFlow.PhoneLogin);
        var user = state.UserId is { } userId ? await phone.UserManager.FindByIdAsync(userId) : null;
        if (user is null)
        {
            ErrorMessage = localizer["VerifyOtp.Invalid"].Value;
            return Page();
        }

        var result = await otpService.VerifyAsync(user, Input.Code, options);
        if (result != OtpVerifyResult.Success)
        {
            ErrorMessage = localizer["VerifyOtp.Invalid"].Value;
            return Page();
        }

        await events.PublishAsync(new OtpVerifiedEvent(tenantId, user.Id, MaskFor(state), timeProvider.GetUtcNow()));

        if (!user.PhoneNumberConfirmed)
        {
            user.PhoneNumberConfirmed = true;
            await phone.UserManager.UpdateAsync(user);
            await events.PublishAsync(new PhoneChangedEvent(tenantId, user.Id, MaskFor(state), true, timeProvider.GetUtcNow()));
        }

        if (!user.HasCompleteProfile)
        {
            return RedirectToPage("./CompleteProfile", new
            {
                flow = returnUrlProtector.Tokenize(new AuthFlowState { ReturnUrl = returnUrl, UserId = user.Id, PhoneNumber = state.PhoneNumber }),
            });
        }

        if (state.PhoneNumber is { } e164
            && !phoneLoginRateLimiter.TryRecordLogin(tenantId, e164, out var retryAfter, out var dailyLimitReached))
        {
            ErrorMessage = dailyLimitReached
                ? localizer["VerifyOtp.RateLimitedDaily"].Value
                : string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
                    localizer["VerifyOtp.RateLimited"].Value,
                    RetryAfterText.Format(localizer, retryAfter ?? PhoneOptions.SuccessfulLoginWindow));
            return Page();
        }

        await phone.SignInManager.SignInWithClaimsAsync(user, isPersistent: false,
            [new Claim(HuiaConstants.ClaimTypes.AuthenticationMethod, HuiaConstants.AuthenticationMethods.Sms)]);
        await events.PublishAsync(new UserLoggedInEvent(tenantId, user.Id, HuiaConstants.AuthenticationMethods.Sms, null, timeProvider.GetUtcNow()));

        return await ResolvePostSignUpRedirectAsync(user, returnUrl);
    }

    /// <summary>Re-issues a code against the same subject, then redirects back to this page.</summary>
    /// <returns>A redirect with <c>resent=1</c>.</returns>
    public async Task<IActionResult> OnPostResendAsync()
    {
        if (!IsPhoneLoginEnabled)
        {
            return NotFound();
        }

        var state = returnUrlProtector.Read(Flow);
        if (state?.PhoneNumber is null)
        {
            return NotFound();
        }

        var options = PhoneOptions;
        var tenantId = tenantAccessor.RequireCurrentTenantId();

        if (rateLimiter.TryAcquire(tenantId, state.PhoneNumber))
        {
            if (state.PendingSignupId is { } pendingId)
            {
                var code = otpService.GenerateCode(options);
                if (pendingSignups.Reissue(pendingId, code, options))
                {
                    await smsSender.SendOtpAsync(tenantId, state.PhoneNumber, code, HttpContext.RequestAborted);
                }
            }
            else if (state.UserId is { } userId
                && await flowIdentity.Create(HuiaAuthFlow.PhoneLogin).UserManager.FindByIdAsync(userId) is { } user)
            {
                var code = await otpService.IssueAsync(user, options);
                await smsSender.SendOtpAsync(tenantId, state.PhoneNumber, code, HttpContext.RequestAborted);
            }

            await events.PublishAsync(new OtpRequestedEvent(tenantId, state.UserId, MaskFor(state), true, timeProvider.GetUtcNow()));
        }

        return RedirectToPage(new { flow = Flow, resent = true });
    }

    private string MaskFor(AuthFlowState state) =>
        state.PhoneNumber is null ? "••••" : phoneNumbers.Mask(state.PhoneNumber);

    private void SetHeadings()
    {
        ViewData["Title"] = localizer["VerifyOtp.Title"].Value;
        ViewData["Heading"] = localizer["VerifyOtp.Heading"].Value;
    }

    /// <summary>The verification form field.</summary>
    public sealed class InputModel
    {
        /// <summary>The one-time code.</summary>
        [Required]
        public string Code { get; set; } = string.Empty;
    }
}
