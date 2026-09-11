using System.Security.Claims;
using Finbuckle.MultiTenant.Abstractions;
using Huia.Emails;
using Huia.Events;
using Huia.Headless.Options;
using Huia.Headless.Services;
using Huia.Identity;
using Huia.Keys;
using Huia.Multitenancy;
using Huia.Options;
using Huia.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Huia.Headless.Endpoints;

/// <summary>Minimal API endpoints for Huia Headless authentication.</summary>
public static class HuiaHeadlessEndpoints
{
    /// <summary>Maps the headless identity endpoints under /{tenant}/identity/* and /.well-known/jwks.</summary>
    internal static RouteGroupBuilder MapHuiaHeadlessEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup("identity");

        group.MapPost("register", RegisterAsync);
        group.MapPost("login", LoginAsync);
        group.MapPost("phone/login/start", PhoneLoginStartAsync);
        group.MapPost("phone/login/verify", PhoneLoginVerifyAsync);
        group.MapPost("phone/complete-profile", PhoneCompleteProfileAsync);
        group.MapPost("refresh", RefreshAsync);
        group.MapPost("logout", LogoutAsync).RequireAuthorization(HuiaConstants.Policies.Api);
        group.MapGet("confirmEmail", ConfirmEmailAsync);
        group.MapPost("resendConfirmationEmail", ResendConfirmationEmailAsync);
        group.MapPost("forgotPassword", ForgotPasswordAsync);
        group.MapPost("resetPassword", ResetPasswordAsync);

        endpoints.MapGet(".well-known/jwks.json", GetJwksAsync);
        endpoints.MapGet(".well-known/jwks", GetJwksAsync);

        return group;
    }

    private static async Task<IResult> GetJwksAsync(
        IMultiTenantContextAccessor tenantAccessor,
        IHuiaKeyRing keyRing,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantAccessor.CurrentTenantId();
        if (tenantId is null)
        {
            return Results.NotFound();
        }

        var jwks = await keyRing.GetPublishedJwksAsync(tenantId, cancellationToken);
        var keys = jwks.Select(jwk => new Dictionary<string, string?>
        {
            ["kty"] = jwk.Kty,
            ["use"] = jwk.Use ?? "sig",
            ["kid"] = jwk.Kid,
            ["alg"] = jwk.Alg ?? SecurityAlgorithms.RsaSha256,
            ["n"] = jwk.N,
            ["e"] = jwk.E,
        }).ToList();

        return Results.Ok(new { keys });
    }

    private static async Task<IResult> RegisterAsync(
        [FromBody] RegisterRequest request,
        IMultiTenantContextAccessor tenantAccessor,
        HuiaOptions options,
        HuiaUserManager userManager,
        IHuiaEmailSender emailSender,
        IHuiaEventPublisher events,
        TimeProvider timeProvider,
        HttpContext httpContext)
    {
        var tenantId = tenantAccessor.RequireCurrentTenantId();
        if (!options.Tenants.TryGetValue(tenantId, out var tenant) ||
            !tenant.Authentication.EmailAndPassword.AllowSelfServiceRegistration)
        {
            return Results.NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.BadRequest(new { error = "invalid_request", error_description = "Email and password are required." });
        }

        var user = new HuiaUser
        {
            TenantId = tenantId,
            UserName = request.Email,
            Email = request.Email,
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return Results.BadRequest(new { error = "registration_failed", errors = result.Errors.Select(e => e.Description) });
        }

        await events.PublishAsync(new UserRegisteredEvent(
            tenantId, user.Id, user.UserName, user.Email, HuiaConstants.AuthenticationMethods.Password, timeProvider.GetUtcNow()));

        if (userManager.Options.SignIn.RequireConfirmedEmail)
        {
            var code = await userManager.GenerateEmailConfirmationTokenAsync(user);
            var encodedCode = WebEncoders.Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(code));
            var confirmUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}{httpContext.Request.PathBase}/identity/confirmEmail?userId={Uri.EscapeDataString(user.Id)}&code={encodedCode}";
            await emailSender.SendEmailConfirmationAsync(user, confirmUrl);
        }

        return Results.Ok(new { message = "User registered successfully." });
    }

    private static async Task<IResult> LoginAsync(
        [FromBody] LoginRequest request,
        IMultiTenantContextAccessor tenantAccessor,
        HuiaOptions options,
        HuiaUserManager userManager,
        HuiaSignInManager signInManager,
        IHuiaHeadlessTokenService tokenService,
        IHuiaEventPublisher events,
        TimeProvider timeProvider)
    {
        var tenantId = tenantAccessor.RequireCurrentTenantId();
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.BadRequest(new { error = "invalid_request", error_description = "Email and password are required." });
        }

        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var signInResult = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);

        if (signInResult.IsLockedOut)
        {
            return Results.Problem(statusCode: StatusCodes.Status423Locked, title: "Account is locked out.");
        }

        if (signInResult.RequiresTwoFactor)
        {
            if (!string.IsNullOrEmpty(request.TwoFactorCode))
            {
                var twoFactorValid = await userManager.VerifyTwoFactorTokenAsync(
                    user, userManager.Options.Tokens.AuthenticatorTokenProvider, request.TwoFactorCode);
                if (!twoFactorValid)
                {
                    return Results.Unauthorized();
                }
            }
            else if (!string.IsNullOrEmpty(request.TwoFactorRecoveryCode))
            {
                var redeemResult = await userManager.RedeemTwoFactorRecoveryCodeAsync(user, request.TwoFactorRecoveryCode);
                if (!redeemResult.Succeeded)
                {
                    return Results.Unauthorized();
                }
            }
            else
            {
                return Results.Ok(new { requiresTwoFactor = true });
            }
        }
        else if (!signInResult.Succeeded)
        {
            return Results.Unauthorized();
        }

        await events.PublishAsync(new UserLoggedInEvent(
            tenantId, user.Id, HuiaConstants.AuthenticationMethods.Password, null, timeProvider.GetUtcNow()));

        var tokenResponse = await tokenService.CreateTokenResponseAsync(tenantId, user, HuiaConstants.AuthenticationMethods.Password);
        return Results.Ok(tokenResponse);
    }

    private static async Task<IResult> PhoneLoginStartAsync(
        [FromBody] PhoneStartRequest request,
        IMultiTenantContextAccessor tenantAccessor,
        HuiaOptions options,
        HuiaUserManager userManager,
        IPhoneNumberService phoneNumbers,
        IPhoneLoginRateLimiter phoneLoginRateLimiter,
        IOtpRateLimiter otpRateLimiter,
        IOtpService otpService,
        IPendingPhoneSignup pendingSignups,
        ISmsSender smsSender,
        ICaptchaVerifier captchaVerifier,
        IHuiaEventPublisher events,
        TimeProvider timeProvider,
        HttpContext httpContext)
    {
        var tenantId = tenantAccessor.RequireCurrentTenantId();
        if (!options.Tenants.TryGetValue(tenantId, out var tenant) || !tenant.Authentication.IsPhoneLoginEnabled)
        {
            return Results.NotFound();
        }

        var phoneOptions = tenant.Authentication.Phone ?? new PhoneOptions();
        if (phoneOptions.Captcha == CaptchaMode.Always &&
            !await captchaVerifier.VerifyAsync(request.CaptchaToken, httpContext.RequestAborted))
        {
            return Results.BadRequest(new { error = "invalid_captcha" });
        }

        var defaultCountry = phoneOptions.DefaultCountry;
        if (!phoneNumbers.TryNormalize(request.PhoneNumber, defaultCountry, out var e164))
        {
            return Results.BadRequest(new { error = "invalid_phone_number" });
        }

        if (!otpRateLimiter.TryAcquire(tenantId, e164))
        {
            return Results.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "OTP rate limit exceeded.");
        }

        if (!phoneLoginRateLimiter.CanRecordLogin(tenantId, e164, out _, out _))
        {
            return Results.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Too many phone login requests.");
        }

        var user = await userManager.FindByPhoneNumberAsync(e164);
        var maskedForEvent = phoneNumbers.Mask(e164);
        bool delivered;

        if (user is not null)
        {
            var code = await otpService.IssueAsync(user, phoneOptions);
            delivered = await smsSender.SendOtpAsync(tenantId, e164, code, httpContext.RequestAborted);
            await events.PublishAsync(new OtpRequestedEvent(tenantId, user.Id, maskedForEvent, delivered, timeProvider.GetUtcNow()));
        }
        else if (phoneOptions.AllowAutoProvisioning)
        {
            var code = otpService.GenerateCode(phoneOptions);
            pendingSignups.Create(tenantId, e164, code, phoneOptions);
            delivered = await smsSender.SendOtpAsync(tenantId, e164, code, httpContext.RequestAborted);
            await events.PublishAsync(new OtpRequestedEvent(tenantId, null, maskedForEvent, delivered, timeProvider.GetUtcNow()));
        }
        else
        {
            await events.PublishAsync(new OtpRequestedEvent(tenantId, null, maskedForEvent, false, timeProvider.GetUtcNow()));
        }

        return Results.Ok(new { message = "Verification code sent." });
    }

    private static async Task<IResult> PhoneLoginVerifyAsync(
        [FromBody] PhoneVerifyRequest request,
        IMultiTenantContextAccessor tenantAccessor,
        HuiaOptions options,
        IPhoneNumberService phoneNumbers,
        IOtpService otpService,
        IPendingPhoneSignup pendingSignups,
        IPhoneLoginRateLimiter phoneLoginRateLimiter,
        HuiaUserManager userManager,
        IHuiaHeadlessTokenService tokenService,
        IHuiaEventPublisher events,
        TimeProvider timeProvider)
    {
        var tenantId = tenantAccessor.RequireCurrentTenantId();
        if (!options.Tenants.TryGetValue(tenantId, out var tenant) || !tenant.Authentication.IsPhoneLoginEnabled)
        {
            return Results.NotFound();
        }

        var phoneOptions = tenant.Authentication.Phone ?? new PhoneOptions();
        var defaultCountry = phoneOptions.DefaultCountry;
        if (!phoneNumbers.TryNormalize(request.PhoneNumber, defaultCountry, out var e164))
        {
            return Results.BadRequest(new { error = "invalid_phone_number" });
        }

        var maskedForEvent = phoneNumbers.Mask(e164);
        var user = await userManager.FindByPhoneNumberAsync(e164);

        if (user is not null)
        {
            var result = await otpService.VerifyAsync(user, request.Code, phoneOptions);
            if (result != OtpVerifyResult.Success)
            {
                return Results.Unauthorized();
            }

            await events.PublishAsync(new OtpVerifiedEvent(tenantId, user.Id, maskedForEvent, timeProvider.GetUtcNow()));

            if (!user.PhoneNumberConfirmed)
            {
                user.PhoneNumberConfirmed = true;
                await userManager.UpdateAsync(user);
                await events.PublishAsync(new PhoneChangedEvent(tenantId, user.Id, maskedForEvent, true, timeProvider.GetUtcNow()));
            }
        }
        else if (phoneOptions.AllowAutoProvisioning)
        {
            var pending = pendingSignups.FindByPhoneNumber(tenantId, e164);
            if (pending is null)
            {
                return Results.Unauthorized();
            }

            var result = pendingSignups.Verify(pending.Id, request.Code, phoneOptions);
            if (result != OtpVerifyResult.Success)
            {
                return Results.Unauthorized();
            }

            user = new HuiaUser
            {
                TenantId = tenantId,
                UserName = e164,
                PhoneNumber = e164,
                PhoneNumberConfirmed = true,
            };

            var createResult = await userManager.CreateAsync(user);
            if (!createResult.Succeeded)
            {
                return Results.BadRequest(new { error = "provisioning_failed", errors = createResult.Errors.Select(e => e.Description) });
            }

            await events.PublishAsync(new UserRegisteredEvent(
                tenantId, user.Id, user.UserName, user.Email, HuiaConstants.AuthenticationMethods.Sms, timeProvider.GetUtcNow()));
            await events.PublishAsync(new OtpVerifiedEvent(tenantId, user.Id, maskedForEvent, timeProvider.GetUtcNow()));
            await events.PublishAsync(new PhoneChangedEvent(tenantId, user.Id, maskedForEvent, true, timeProvider.GetUtcNow()));
        }
        else
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Phone number not registered.");
        }

        if (!user.HasCompleteProfile)
        {
            var provisionalToken = tokenService.CreateProvisionalToken(tenantId, user.Id);
            return Results.Ok(new
            {
                requiresProfileCompletion = true,
                provisionalToken,
            });
        }

        if (!phoneLoginRateLimiter.TryRecordLogin(tenantId, e164, out _, out _))
        {
            return Results.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Too many phone login requests.");
        }

        await events.PublishAsync(new UserLoggedInEvent(
            tenantId, user.Id, HuiaConstants.AuthenticationMethods.Sms, null, timeProvider.GetUtcNow()));

        var tokens = await tokenService.CreateTokenResponseAsync(tenantId, user, HuiaConstants.AuthenticationMethods.Sms);
        return Results.Ok(tokens);
    }

    private static async Task<IResult> PhoneCompleteProfileAsync(
        [FromBody] CompleteProfileRequest request,
        IMultiTenantContextAccessor tenantAccessor,
        HuiaUserManager userManager,
        IPhoneLoginRateLimiter phoneLoginRateLimiter,
        IHuiaHeadlessTokenService tokenService,
        IHuiaEventPublisher events,
        TimeProvider timeProvider)
    {
        var tenantId = tenantAccessor.RequireCurrentTenantId();
        var validation = tokenService.ValidateProvisionalToken(request.ProvisionalToken);
        if (validation is null || !string.Equals(validation.Value.TenantId, tenantId, StringComparison.Ordinal))
        {
            return Results.Unauthorized();
        }

        var user = await userManager.FindByIdAsync(validation.Value.UserId);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        user.FirstName = request.FirstName?.Trim() ?? string.Empty;
        user.LastName = request.LastName?.Trim() ?? string.Empty;

        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return Results.BadRequest(new { error = "profile_update_failed", errors = updateResult.Errors.Select(e => e.Description) });
        }

        if (user.PhoneNumber is { } e164)
        {
            phoneLoginRateLimiter.TryRecordLogin(tenantId, e164, out _, out _);
        }

        await events.PublishAsync(new UserLoggedInEvent(
            tenantId, user.Id, HuiaConstants.AuthenticationMethods.Sms, null, timeProvider.GetUtcNow()));

        var tokens = await tokenService.CreateTokenResponseAsync(tenantId, user, HuiaConstants.AuthenticationMethods.Sms);
        return Results.Ok(tokens);
    }

    private static async Task<IResult> RefreshAsync(
        [FromBody] RefreshRequest request,
        IMultiTenantContextAccessor tenantAccessor,
        HuiaOptions options,
        HuiaUserManager userManager,
        IHuiaHeadlessRefreshTokenStore refreshTokenStore,
        IHuiaHeadlessTokenService tokenService)
    {
        var tenantId = tenantAccessor.RequireCurrentTenantId();
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return Results.BadRequest(new { error = "invalid_request", error_description = "RefreshToken is required." });
        }

        options.Tenants.TryGetValue(tenantId, out var tenant);
        var headlessOptions = tenant?.GetHuiaHeadless();
        var lifetime = headlessOptions?.RefreshToken.Lifetime ?? TimeSpan.FromDays(30);
        var sliding = headlessOptions?.RefreshToken.SlidingExpiration ?? true;

        var rotation = await refreshTokenStore.RotateAsync(tenantId, request.RefreshToken, lifetime, sliding);
        if (!rotation.Succeeded || rotation.UserId is null)
        {
            return Results.Unauthorized();
        }

        var user = await userManager.FindByIdAsync(rotation.UserId);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var tokenResponse = await tokenService.CreateTokenResponseAsync(
            tenantId, user, HuiaConstants.AuthenticationMethods.Password);

        return Results.Ok(new HuiaTokenResponse
        {
            TokenType = HuiaHeadlessConstants.TokenTypes.Bearer,
            AccessToken = tokenResponse.AccessToken,
            RefreshToken = rotation.NewRawRefreshToken!,
            ExpiresIn = tokenResponse.ExpiresIn,
        });
    }

    private static async Task<IResult> LogoutAsync(
        [FromBody] LogoutRequest? request,
        IMultiTenantContextAccessor tenantAccessor,
        ClaimsPrincipal principal,
        IHuiaHeadlessRefreshTokenStore refreshTokenStore)
    {
        var tenantId = tenantAccessor.RequireCurrentTenantId();
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");

        if (!string.IsNullOrWhiteSpace(request?.RefreshToken))
        {
            await refreshTokenStore.RevokeFamilyAsync(tenantId, request.RefreshToken);
        }
        else if (!string.IsNullOrWhiteSpace(userId))
        {
            await refreshTokenStore.RevokeUserSessionsAsync(tenantId, userId);
        }

        return Results.Ok(new { message = "Logged out successfully." });
    }

    private static async Task<IResult> ConfirmEmailAsync(
        [FromQuery] string userId,
        [FromQuery] string code,
        HuiaUserManager userManager)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(code))
        {
            return Results.BadRequest(new { error = "invalid_request" });
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return Results.NotFound();
        }

        var decodedCode = System.Text.Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
        var result = await userManager.ConfirmEmailAsync(user, decodedCode);
        if (!result.Succeeded)
        {
            return Results.BadRequest(new { error = "confirm_email_failed", errors = result.Errors.Select(e => e.Description) });
        }

        return Results.Ok(new { message = "Email confirmed successfully." });
    }

    private static async Task<IResult> ResendConfirmationEmailAsync(
        [FromBody] ResendConfirmationEmailRequest request,
        HuiaUserManager userManager,
        IHuiaEmailSender emailSender,
        HttpContext httpContext)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return Results.BadRequest(new { error = "invalid_request" });
        }

        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is not null && !user.EmailConfirmed)
        {
            var code = await userManager.GenerateEmailConfirmationTokenAsync(user);
            var encodedCode = WebEncoders.Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(code));
            var confirmUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}{httpContext.Request.PathBase}/identity/confirmEmail?userId={Uri.EscapeDataString(user.Id)}&code={encodedCode}";
            await emailSender.SendEmailConfirmationAsync(user, confirmUrl);
        }

        return Results.Ok(new { message = "If the email exists and is unconfirmed, a new confirmation link was sent." });
    }

    private static async Task<IResult> ForgotPasswordAsync(
        [FromBody] ForgotPasswordRequest request,
        HuiaUserManager userManager,
        IHuiaEmailSender emailSender,
        HttpContext httpContext)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return Results.BadRequest(new { error = "invalid_request" });
        }

        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is not null && await userManager.IsEmailConfirmedAsync(user))
        {
            var code = await userManager.GeneratePasswordResetTokenAsync(user);
            var encodedCode = WebEncoders.Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(code));
            var resetUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}{httpContext.Request.PathBase}/identity/resetPassword?code={encodedCode}&email={Uri.EscapeDataString(user.Email!)}";
            await emailSender.SendPasswordResetAsync(user, resetUrl);
        }

        return Results.Ok(new { message = "If the email is recognized, a password reset link has been sent." });
    }

    private static async Task<IResult> ResetPasswordAsync(
        [FromBody] ResetPasswordRequest request,
        IMultiTenantContextAccessor tenantAccessor,
        HuiaUserManager userManager,
        IHuiaEventPublisher events,
        TimeProvider timeProvider)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.ResetCode) || string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return Results.BadRequest(new { error = "invalid_request" });
        }

        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return Results.BadRequest(new { error = "invalid_request" });
        }

        var decodedCode = System.Text.Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(request.ResetCode));
        var result = await userManager.ResetPasswordAsync(user, decodedCode, request.NewPassword);
        if (!result.Succeeded)
        {
            return Results.BadRequest(new { error = "reset_failed", errors = result.Errors.Select(e => e.Description) });
        }

        var tenantId = tenantAccessor.RequireCurrentTenantId();
        await events.PublishAsync(new PasswordChangedEvent(tenantId, user.Id, Reset: true, timeProvider.GetUtcNow()));

        return Results.Ok(new { message = "Password reset successfully." });
    }

    /// <summary>Request payload for user registration.</summary>
    public sealed record RegisterRequest(string Email, string Password);

    /// <summary>Request payload for user login.</summary>
    public sealed record LoginRequest(string Email, string Password, string? TwoFactorCode = null, string? TwoFactorRecoveryCode = null);

    /// <summary>Request payload for starting phone login.</summary>
    public sealed record PhoneStartRequest(string PhoneNumber, string? CaptchaToken = null);

    /// <summary>Request payload for verifying phone login OTP code.</summary>
    public sealed record PhoneVerifyRequest(string PhoneNumber, string Code);

    /// <summary>Request payload for completing profile on phone login.</summary>
    public sealed record CompleteProfileRequest(string ProvisionalToken, string FirstName, string LastName);

    /// <summary>Request payload for refreshing tokens.</summary>
    public sealed record RefreshRequest(string RefreshToken);

    /// <summary>Request payload for logging out.</summary>
    public sealed record LogoutRequest(string? RefreshToken = null);

    /// <summary>Request payload for resending confirmation email.</summary>
    public sealed record ResendConfirmationEmailRequest(string Email);

    /// <summary>Request payload for requesting password reset.</summary>
    public sealed record ForgotPasswordRequest(string Email);

    /// <summary>Request payload for completing password reset.</summary>
    public sealed record ResetPasswordRequest(string Email, string ResetCode, string NewPassword);
}
