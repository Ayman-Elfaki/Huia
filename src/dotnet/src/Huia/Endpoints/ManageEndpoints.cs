using System.Security.Claims;
using System.Text;
using Finbuckle.MultiTenant.Abstractions;
using Huia.Emails;
using Huia.Events;
using Huia.Identity;
using Huia.Multitenancy;
using Huia.Options;
using Huia.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;

namespace Huia.Endpoints;

/// <summary>
/// The token-protected self-service API (<c>/manage/*</c>). Every route requires the <c>Huia:Api</c>
/// policy. The caller is resolved from the token's <c>sub</c> or <see cref="ClaimTypes.NameIdentifier"/> claim.
/// </summary>
internal static class ManageEndpoints
{
    private const string PendingPhoneToken = "pending_phone";

    public static RouteGroupBuilder MapHuiaManageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("manage");
        group.RequireAuthorization(HuiaConstants.Policies.Api);

        group.MapGet("profile", GetProfileAsync);
        group.MapPut("profile", UpdateProfileAsync);
        group.MapGet("email", GetEmailAsync);
        group.MapPut("email", ChangeEmailAsync);
        group.MapPost("email/confirm", SendEmailConfirmationAsync);
        group.MapPut("password", ChangePasswordAsync);
        group.MapGet("phone", GetPhoneAsync);
        group.MapPut("phone", StartPhoneChangeAsync);
        group.MapPost("phone/confirm", ConfirmPhoneChangeAsync);
        group.MapDelete("phone", RemovePhoneAsync);

        return group;
    }

    private static async Task<IResult> GetProfileAsync(HttpContext context, HuiaUserManager userManager)
    {
        var user = await ResolveUserAsync(context, userManager);
        return user is null
            ? Results.Unauthorized()
            : Results.Ok(new ProfileDto(user.FirstName, user.LastName, user.Email, user.PhoneNumber, user.PhoneNumberConfirmed));
    }

    private static async Task<IResult> UpdateProfileAsync(HttpContext context, HuiaUserManager userManager, UpdateProfileRequest body)
    {
        var user = await ResolveUserAsync(context, userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(body.FirstName) || string.IsNullOrWhiteSpace(body.LastName))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["name"] = ["First and last name are required."],
            });
        }

        user.FirstName = body.FirstName.Trim();
        user.LastName = body.LastName.Trim();
        var result = await userManager.UpdateAsync(user);
        return result.Succeeded ? Results.NoContent() : Problem(result);
    }

    private static async Task<IResult> GetEmailAsync(HttpContext context, HuiaUserManager userManager)
    {
        var user = await ResolveUserAsync(context, userManager);
        return user is null ? Results.Unauthorized() : Results.Ok(new EmailDto(user.Email, user.EmailConfirmed));
    }

    private static async Task<IResult> ChangeEmailAsync(
        HttpContext context, HuiaUserManager userManager, IHuiaEmailSender emailSender, ChangeEmailRequest body)
    {
        var user = await ResolveUserAsync(context, userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(body.NewEmail))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["newEmail"] = ["An email address is required."] });
        }

        if (await userManager.GetUserTypeAsync(user) == HuiaUserType.Phone)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["newEmail"] = ["A phone-login account cannot set an email address."],
            });
        }

        user.Email = body.NewEmail.Trim();
        user.EmailConfirmed = false;
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return Problem(result);
        }

        await SendConfirmationEmailAsync(context, userManager, emailSender, user);
        return Results.Accepted();
    }

    private static async Task<IResult> SendEmailConfirmationAsync(
        HttpContext context, HuiaUserManager userManager, IHuiaEmailSender emailSender)
    {
        var user = await ResolveUserAsync(context, userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        if (await userManager.GetUserTypeAsync(user) == HuiaUserType.Phone)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["email"] = ["A phone-login account has no email address to confirm."],
            });
        }

        if (string.IsNullOrEmpty(user.Email))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["email"] = ["No email address is set."] });
        }

        if (user.EmailConfirmed)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["email"] = ["This email address is already confirmed."],
            });
        }

        await SendConfirmationEmailAsync(context, userManager, emailSender, user);
        return Results.Accepted();
    }

    private static async Task SendConfirmationEmailAsync(
        HttpContext context, HuiaUserManager userManager, IHuiaEmailSender emailSender, HuiaUser user)
    {
        var rawToken = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(rawToken));
        var confirmUrl = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.PathBase}" +
                         $"/identity/account/confirmemail?userId={Uri.EscapeDataString(user.Id)}&code={code}";
        await emailSender.SendEmailConfirmationAsync(user, confirmUrl, context.RequestAborted);
    }

    private static async Task<IResult> ChangePasswordAsync(
        HttpContext context, HuiaUserManager userManager, IMultiTenantContextAccessor tenantAccessor,
        IHuiaEventPublisher events, TimeProvider timeProvider, ChangePasswordRequest body)
    {
        var user = await ResolveUserAsync(context, userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var result = await userManager.ChangePasswordAsync(user, body.CurrentPassword, body.NewPassword);
        if (!result.Succeeded)
        {
            return Problem(result);
        }

        await events.PublishAsync(new PasswordChangedEvent(
            tenantAccessor.RequireCurrentTenantId(), user.Id, Reset: false, timeProvider.GetUtcNow()));
        return Results.NoContent();
    }

    private static async Task<IResult> GetPhoneAsync(HttpContext context, HuiaUserManager userManager)
    {
        var user = await ResolveUserAsync(context, userManager);
        return user is null ? Results.Unauthorized() : Results.Ok(new PhoneDto(user.PhoneNumber, user.PhoneNumberConfirmed));
    }

    private static async Task<IResult> StartPhoneChangeAsync(
        HttpContext context, HuiaUserManager userManager, IOtpService otpService, ISmsSender smsSender,
        IPhoneNumberService phoneNumbers, IMultiTenantContextAccessor tenantAccessor, HuiaOptions huiaOptions,
        IHuiaEventPublisher events, TimeProvider timeProvider, ChangePhoneRequest body)
    {
        var user = await ResolveUserAsync(context, userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        if (await userManager.GetUserTypeAsync(user) is HuiaUserType.Password or HuiaUserType.External)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["phoneNumber"] = ["This account type cannot set a phone number."],
            });
        }

        var tenantId = tenantAccessor.RequireCurrentTenantId();
        var options = PhoneOptions(huiaOptions, tenantId);

        if (!phoneNumbers.TryNormalize(body.PhoneNumber, null, out var e164))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["phoneNumber"] = ["A valid E.164 number is required."] });
        }

        await userManager.SetAuthenticationTokenAsync(user, HuiaConstants.PasswordlessLoginProvider, PendingPhoneToken, e164);
        var code = await otpService.IssueAsync(user, options);
        var delivered = await smsSender.SendOtpAsync(tenantId, e164, code, context.RequestAborted);

        await events.PublishAsync(new OtpRequestedEvent(tenantId, user.Id, phoneNumbers.Mask(e164), delivered, timeProvider.GetUtcNow()));
        return Results.Accepted();
    }

    private static async Task<IResult> ConfirmPhoneChangeAsync(
        HttpContext context, HuiaUserManager userManager, IOtpService otpService, IPhoneNumberService phoneNumbers,
        IMultiTenantContextAccessor tenantAccessor, HuiaOptions huiaOptions, IHuiaEventPublisher events, TimeProvider timeProvider,
        ConfirmPhoneRequest body)
    {
        var user = await ResolveUserAsync(context, userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var userType = await userManager.GetUserTypeAsync(user);
        if (userType is HuiaUserType.Password or HuiaUserType.External)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["phoneNumber"] = ["This account type cannot set a phone number."],
            });
        }

        var tenantId = tenantAccessor.RequireCurrentTenantId();
        var options = PhoneOptions(huiaOptions, tenantId);

        var pending = await userManager.GetAuthenticationTokenAsync(user, HuiaConstants.PasswordlessLoginProvider, PendingPhoneToken);
        if (string.IsNullOrEmpty(pending))
        {
            return Results.Conflict(new { error = "no_pending_change" });
        }

        var verify = await otpService.VerifyAsync(user, body.Code, options);
        if (verify != OtpVerifyResult.Success)
        {
            return Results.BadRequest(new { error = verify.ToString().ToLowerInvariant() });
        }

        user.PhoneNumber = pending;
        user.PhoneNumberConfirmed = true;

        if (userType == HuiaUserType.Phone)
        {
            var rename = await userManager.SetUserNameAsync(user, pending);
            if (!rename.Succeeded)
            {
                return Problem(rename);
            }
        }

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return Problem(result);
        }

        await userManager.RemoveAuthenticationTokenAsync(user, HuiaConstants.PasswordlessLoginProvider, PendingPhoneToken);
        await events.PublishAsync(new PhoneChangedEvent(tenantId, user.Id, phoneNumbers.Mask(pending), true, timeProvider.GetUtcNow()));
        return Results.NoContent();
    }

    private static async Task<IResult> RemovePhoneAsync(
        HttpContext context, HuiaUserManager userManager, IMultiTenantContextAccessor tenantAccessor,
        IHuiaEventPublisher events, TimeProvider timeProvider)
    {
        var user = await ResolveUserAsync(context, userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        if (await userManager.GetUserTypeAsync(user) == HuiaUserType.Phone)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["phoneNumber"] = ["A phone-login account cannot remove its phone number."],
            });
        }

        user.PhoneNumber = null;
        user.PhoneNumberConfirmed = false;
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return Problem(result);
        }

        await events.PublishAsync(new PhoneChangedEvent(
            tenantAccessor.RequireCurrentTenantId(), user.Id, PhoneNumberMask: null, Confirmed: false, timeProvider.GetUtcNow()));
        return Results.NoContent();
    }

    internal static async Task<HuiaUser?> ResolveUserAsync(HttpContext context, HuiaUserManager userManager)
    {
        var principal = context.User;
        var subject = principal.FindFirstValue("sub")
                      ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return subject is null ? null : await userManager.FindByIdAsync(subject);
    }

    private static PhoneOptions PhoneOptions(HuiaOptions options, string tenantId) =>
        options.Tenants.TryGetValue(tenantId, out var tenant)
            ? tenant.Authentication.Phone ?? new PhoneOptions()
            : new PhoneOptions();

    private static IResult Problem(IdentityResult result) =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["identity"] = result.Errors.Select(e => e.Description).ToArray(),
        });

    private sealed record ProfileDto(string FirstName, string LastName, string? Email, string? PhoneNumber, bool PhoneNumberConfirmed);

    private sealed record EmailDto(string? Email, bool Confirmed);

    private sealed record PhoneDto(string? PhoneNumber, bool Confirmed);

    /// <summary>Body of <c>PUT /manage/profile</c>.</summary>
    /// <param name="FirstName">The new given name.</param>
    /// <param name="LastName">The new family name.</param>
    public sealed record UpdateProfileRequest(string FirstName, string LastName);

    /// <summary>Body of <c>PUT /manage/email</c>.</summary>
    /// <param name="NewEmail">The new email address (requires re-confirmation).</param>
    public sealed record ChangeEmailRequest(string NewEmail);

    /// <summary>Body of <c>PUT /manage/password</c>.</summary>
    /// <param name="CurrentPassword">The current password.</param>
    /// <param name="NewPassword">The new password.</param>
    public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

    /// <summary>Body of <c>PUT /manage/phone</c>.</summary>
    /// <param name="PhoneNumber">The new phone number to verify.</param>
    public sealed record ChangePhoneRequest(string PhoneNumber);

    /// <summary>Body of <c>POST /manage/phone/confirm</c>.</summary>
    /// <param name="Code">The one-time code sent to the new number.</param>
    public sealed record ConfirmPhoneRequest(string Code);
}
