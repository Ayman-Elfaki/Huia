using System.Security.Claims;
using Huia.Entities;
using Huia.Events;
using Huia.Headless.Identity;
using Huia.Headless.Services;
using Huia.Identity;
using Huia.Multitenancy;
using Huia.Options;
using Huia.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;

namespace Huia.Headless.Endpoints;

/// <summary>
/// Passwordless SMS one-time-code endpoints for the single-tenant Headless flavor — the JSON equivalent
/// of <c>Huia.OpenId</c>'s <c>Login</c>/<c>VerifyOtp</c>/<c>CompleteProfile</c> Razor pages. There is no
/// page navigation to carry state across steps, so <see cref="IPhoneLoginFlowStore"/> plays the role
/// <c>Huia.OpenId</c>'s encrypted flow token plays there: an opaque, server-held handle the client passes
/// back on the next call. Every route 404s unless the host's one tenant has called
/// <see cref="HuiaTenantAuthenticationOptions.UsePhoneLogin"/>.
/// </summary>
internal static class PhoneEndpoints
{
    public static void MapHuiaHeadlessPhoneEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("identity/phone");
        group.MapPost("start", StartAsync).WithName("huia.headless.phone.start");
        group.MapPost("verify", VerifyAsync).WithName("huia.headless.phone.verify");
        group.MapPost("complete-profile", CompleteProfileAsync).WithName("huia.headless.phone.complete-profile");
    }

    private static async Task<IResult> StartAsync(
        HttpContext context, HuiaUserManager userManager, IPhoneNumberService phoneNumbers,
        IOtpRateLimiter rateLimiter, IPhoneLoginRateLimiter phoneLoginRateLimiter, IOtpService<HuiaUser> otpService,
        IPendingPhoneSignup pendingSignups, IPhoneLoginFlowStore flows, ISmsSender smsSender,
        ICaptchaVerifier captcha, IHuiaTenantContext tenantContext, TenantOptions tenant,
        IHuiaEventPublisher events, TimeProvider timeProvider, StartPhoneLoginRequest body)
    {
        var tenantId = tenantContext.CurrentTenantId;
        var phoneOptions = tenant.Authentication.Phone;
        if (phoneOptions is null)
        {
            return Results.NotFound();
        }

        if (phoneOptions.Captcha == CaptchaMode.Always && !await captcha.VerifyAsync(body.CaptchaResponse, context.RequestAborted))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["captcha"] = ["A CAPTCHA response is required."] });
        }

        if (!phoneNumbers.TryNormalize(body.PhoneNumber, body.Country, out var e164))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["phoneNumber"] = ["A valid phone number is required."] });
        }

        if (!rateLimiter.TryAcquire(tenantId, e164))
        {
            return Problem("rate_limited", null);
        }

        // The successful-sign-in ceiling is checked here, before an SMS is spent — the actual permit is
        // consumed later, once verification succeeds.
        if (!phoneLoginRateLimiter.CanRecordLogin(tenantId, e164, out var retryAfter, out var dailyLimitReached))
        {
            return Problem(dailyLimitReached ? "daily_limit" : "rate_limited", retryAfter);
        }

        var user = await userManager.FindByPhoneNumberAsync(e164);
        var maskedForEvent = phoneNumbers.Mask(e164);
        string flowId;
        bool delivered;

        if (user is not null)
        {
            var code = await otpService.IssueAsync(user, phoneOptions);
            delivered = await smsSender.SendOtpAsync(tenantId, e164, code, context.RequestAborted);
            flowId = flows.Create(e164, user.Id, null);
            await events.PublishAsync(new OtpRequestedEvent(tenantId, user.Id, maskedForEvent, delivered, timeProvider.GetUtcNow()));
        }
        else if (phoneOptions.AllowAutoProvisioning)
        {
            var code = otpService.GenerateCode(phoneOptions);
            var pendingId = pendingSignups.Create(tenantId, e164, code, phoneOptions);
            delivered = await smsSender.SendOtpAsync(tenantId, e164, code, context.RequestAborted);
            flowId = flows.Create(e164, null, pendingId);
            await events.PublishAsync(new OtpRequestedEvent(tenantId, null, maskedForEvent, delivered, timeProvider.GetUtcNow()));
        }
        else
        {
            // Unknown number, auto-provisioning off: still hand back a flow id and behave identically on
            // verify, to avoid letting a client distinguish "no such number" from "code sent".
            flowId = flows.Create(e164, null, null);
            await events.PublishAsync(new OtpRequestedEvent(tenantId, null, maskedForEvent, false, timeProvider.GetUtcNow()));
        }

        return Results.Ok(new StartPhoneLoginResponse(flowId));
    }

    private static async Task<IResult> VerifyAsync(
        HttpContext context, HuiaUserManager userManager, HuiaSignInManager<HuiaUser> signInManager,
        IPhoneLoginFlowStore flows, IPendingPhoneSignup pendingSignups, IOtpService<HuiaUser> otpService,
        IPhoneLoginRateLimiter phoneLoginRateLimiter, IHuiaTenantContext tenantContext, TenantOptions tenant,
        IHuiaEventPublisher events, TimeProvider timeProvider, VerifyPhoneLoginRequest body)
    {
        var tenantId = tenantContext.CurrentTenantId;
        var phoneOptions = tenant.Authentication.Phone;
        if (phoneOptions is null)
        {
            return Results.NotFound();
        }

        var flow = flows.Get(body.FlowId);
        if (flow is null)
        {
            return Problem("invalid", null);
        }

        if (flow.PendingSignupId is { } pendingId)
        {
            var pendingResult = pendingSignups.Verify(pendingId, body.Code, phoneOptions);
            if (pendingResult != OtpVerifyResult.Success)
            {
                return Problem(ToErrorCode(pendingResult), null);
            }

            await events.PublishAsync(new OtpVerifiedEvent(tenantId, null, MaskOf(flow.PhoneNumber), timeProvider.GetUtcNow()));
            flows.MarkVerified(flow.Id);
            return Results.Ok(new VerifyPhoneLoginResponse(FlowId: flow.Id, RequiresProfile: true));
        }

        if (flow.UserId is not { } userId)
        {
            // The "unknown number, auto-provisioning off" case from StartAsync — behave as an invalid code.
            return Problem("invalid", null);
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return Problem("invalid", null);
        }

        var result = await otpService.VerifyAsync(user, body.Code, phoneOptions);
        if (result != OtpVerifyResult.Success)
        {
            return Problem(ToErrorCode(result), null);
        }

        await events.PublishAsync(new OtpVerifiedEvent(tenantId, user.Id, MaskOf(flow.PhoneNumber), timeProvider.GetUtcNow()));

        if (!user.PhoneNumberConfirmed)
        {
            user.PhoneNumberConfirmed = true;
            await userManager.UpdateAsync(user);
            await events.PublishAsync(new PhoneChangedEvent(tenantId, user.Id, MaskOf(flow.PhoneNumber), true, timeProvider.GetUtcNow()));
        }

        if (!user.HasCompleteProfile)
        {
            flows.MarkVerified(flow.Id);
            return Results.Ok(new VerifyPhoneLoginResponse(FlowId: flow.Id, RequiresProfile: true));
        }

        if (!phoneLoginRateLimiter.TryRecordLogin(tenantId, flow.PhoneNumber, out var retryAfter, out var dailyLimitReached))
        {
            return Problem(dailyLimitReached ? "daily_limit" : "rate_limited", retryAfter);
        }

        flows.Remove(flow.Id);
        await SignInAsync(signInManager, events, user, tenantId, timeProvider);
        return Results.Empty;
    }

    private static async Task<IResult> CompleteProfileAsync(
        HttpContext context, HuiaUserManager userManager, HuiaSignInManager<HuiaUser> signInManager,
        IPhoneLoginFlowStore flows, IPendingPhoneSignup pendingSignups, IPhoneLoginRateLimiter phoneLoginRateLimiter,
        IHuiaTenantContext tenantContext, IHuiaEventPublisher events, TimeProvider timeProvider,
        CompletePhoneProfileRequest body)
    {
        var tenantId = tenantContext.CurrentTenantId;

        var flow = flows.Get(body.FlowId);
        if (flow is null || !flow.Verified)
        {
            return Problem("invalid", null);
        }

        HuiaUser user;
        if (flow.PendingSignupId is { } pendingId)
        {
            var pending = pendingSignups.Get(pendingId);
            var phoneNumber = pending?.PhoneNumber ?? flow.PhoneNumber;

            var (create, created) = await userManager.CreatePhoneUserAsync(tenantId, phoneNumber, body.FirstName, body.LastName);
            if (!create.Succeeded)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["identity"] = create.Errors.Select(e => e.Description).ToArray(),
                });
            }

            pendingSignups.Remove(pendingId);
            user = created;
            await events.PublishAsync(new UserRegisteredEvent(
                tenantId, user.Id, user.UserName!, null, HuiaConstants.AuthenticationMethods.Sms, timeProvider.GetUtcNow()));
        }
        else if (flow.UserId is { } userId)
        {
            var existing = await userManager.FindByIdAsync(userId);
            if (existing is null)
            {
                return Problem("invalid", null);
            }

            existing.FirstName = body.FirstName;
            existing.LastName = body.LastName;
            var update = await userManager.UpdateAsync(existing);
            if (!update.Succeeded)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["identity"] = update.Errors.Select(e => e.Description).ToArray(),
                });
            }

            user = existing;
        }
        else
        {
            return Problem("invalid", null);
        }

        if (!phoneLoginRateLimiter.TryRecordLogin(tenantId, flow.PhoneNumber, out var retryAfter, out var dailyLimitReached))
        {
            return Problem(dailyLimitReached ? "daily_limit" : "rate_limited", retryAfter);
        }

        flows.Remove(flow.Id);
        await SignInAsync(signInManager, events, user, tenantId, timeProvider);
        return Results.Empty;
    }

    private static async Task SignInAsync(
        HuiaSignInManager<HuiaUser> signInManager, IHuiaEventPublisher events, HuiaUser user, string tenantId, TimeProvider timeProvider)
    {
        // Bearer scheme, not the cookie default — SignInAsync's BearerTokenHandler writes the
        // {accessToken, refreshToken, expiresIn} response body itself, the same way MapIdentityApi's own
        // /login does; there is no interactive session to redirect from.
        signInManager.AuthenticationScheme = IdentityConstants.BearerScheme;
        await signInManager.SignInWithClaimsAsync(user, isPersistent: false,
            [new Claim(HuiaConstants.ClaimTypes.AuthenticationMethod, HuiaConstants.AuthenticationMethods.Sms)]);
        await events.PublishAsync(new UserLoggedInEvent(tenantId, user.Id, HuiaConstants.AuthenticationMethods.Sms, null, timeProvider.GetUtcNow()));
    }

    private static string MaskOf(string e164) =>
        e164.Length <= 4 ? "••••" : "••••" + e164[^4..];

    private static string ToErrorCode(OtpVerifyResult result) => result switch
    {
        OtpVerifyResult.Expired => "expired",
        OtpVerifyResult.TooManyAttempts => "too_many_attempts",
        OtpVerifyResult.NotFound => "not_found",
        _ => "invalid",
    };

    private static IResult Problem(string error, TimeSpan? retryAfter) =>
        Results.Json(new { error, retryAfterSeconds = retryAfter is { } r ? (int)Math.Ceiling(r.TotalSeconds) : (int?)null },
            statusCode: error is "rate_limited" or "daily_limit" ? StatusCodes.Status429TooManyRequests : StatusCodes.Status400BadRequest);

    /// <summary>Body of <c>POST identity/phone/start</c>.</summary>
    /// <param name="PhoneNumber">The number as entered (E.164 or national).</param>
    /// <param name="Country">The ISO country to interpret a national number, when known.</param>
    /// <param name="CaptchaResponse">The CAPTCHA response token, when the tenant requires one.</param>
    public sealed record StartPhoneLoginRequest(string PhoneNumber, string? Country, string? CaptchaResponse);

    /// <summary>Response of <c>POST identity/phone/start</c>.</summary>
    /// <param name="FlowId">Opaque handle for the following <c>verify</c> call.</param>
    public sealed record StartPhoneLoginResponse(string FlowId);

    /// <summary>Body of <c>POST identity/phone/verify</c>.</summary>
    /// <param name="FlowId">The handle returned by <c>start</c>.</param>
    /// <param name="Code">The one-time code.</param>
    public sealed record VerifyPhoneLoginRequest(string FlowId, string Code);

    /// <summary>Response of <c>POST identity/phone/verify</c> when the account still needs a name.</summary>
    /// <param name="FlowId">Pass this to <c>complete-profile</c>.</param>
    /// <param name="RequiresProfile">Always <see langword="true"/> — otherwise the bearer token response body is returned instead.</param>
    public sealed record VerifyPhoneLoginResponse(string FlowId, bool RequiresProfile);

    /// <summary>Body of <c>POST identity/phone/complete-profile</c>.</summary>
    /// <param name="FlowId">The handle from a <c>verify</c> response with <c>requiresProfile: true</c>.</param>
    /// <param name="FirstName">The account's first name.</param>
    /// <param name="LastName">The account's last name.</param>
    public sealed record CompletePhoneProfileRequest(string FlowId, string FirstName, string LastName);
}
