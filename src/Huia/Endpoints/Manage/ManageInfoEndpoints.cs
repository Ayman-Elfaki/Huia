using System.Security.Claims;
using System.Text;
using Huia.Common;
using Huia.Identity;
using Huia.Localization;
using Huia.Passwordless;
using Huia.Sms;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Localization;

namespace Huia.Endpoints.Manage;

/// <summary>
/// JSON endpoints for a signed-in user to manage their own account (two-factor, email, password) — for an
/// SPA/native client to build its own account-settings UI against. Registration, sign-in, and password
/// recovery are server-rendered pages instead (see <c>Huia</c>), not JSON APIs: OpenIddict's
/// <c>/connect/authorize</c> needs a real browser session (the <c>Identity.Application</c> cookie) to exist
/// before it can issue a code, so there's no JSON client that would call a login endpoint directly.
/// </summary>
internal static class ManageInfoEndpoints
{
    public static RouteGroupBuilder MapManageInfoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/info");

        group.MapGet("", GetInfoAsync);
        group.MapPost("", UpdateInfoAsync);
        group.MapPost("/phone", RequestPhoneChangeAsync);
        group.MapPost("/phone/verify", VerifyPhoneChangeAsync);
        group.MapDelete("/phone", RemovePhoneAsync);

        return group;
    }

    private static async Task<IResult> GetInfoAsync(ClaimsPrincipal principal, UserManager<HuiaUser> userManager)
    {
        var user = await userManager.GetSignedInUserAsync(principal).ConfigureAwait(false);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(new InfoResponse(user.Email,
            await userManager.IsEmailConfirmedAsync(user).ConfigureAwait(false), user.PhoneNumber,
            await userManager.IsPhoneNumberConfirmedAsync(user).ConfigureAwait(false), user.FirstName, user.LastName,
            user.Picture));
    }

    // One line over MA0051's default 60-line limit; splitting this single linear update-then-notify flow
    // wouldn't reduce its actual complexity, just spread it across an extra method.
#pragma warning disable MA0051
    private static async Task<IResult> UpdateInfoAsync(UpdateInfoRequest request, ClaimsPrincipal principal,
        UserManager<HuiaUser> userManager, IEmailSender<HuiaUser> emailSender, HttpContext httpContext)
    {
        var user = await userManager.GetSignedInUserAsync(principal).ConfigureAwait(false);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        // FirstName/LastName is a partial update: omitting the field (null) leaves the existing value alone,
        // but a field that IS included must still be a valid name - it can't be blanked out to empty/invalid
        // via this endpoint. Minimal API records aren't DataAnnotations-validated automatically in this
        // codebase (unlike the Identity UI's Razor Page InputModels, see PersonNameAttribute), so this checks
        // explicitly.
        if (request.FirstName is not null && !PersonNameValidator.IsValid(request.FirstName))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
                { [nameof(request.FirstName)] = [PersonNameValidator.DescribeError("First name")] });
        }

        if (request.LastName is not null && !PersonNameValidator.IsValid(request.LastName))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
                { [nameof(request.LastName)] = [PersonNameValidator.DescribeError("Last name")] });
        }

        if (request.FirstName is not null)
        {
            user.FirstName = request.FirstName;
        }

        if (request.LastName is not null)
        {
            user.LastName = request.LastName;
        }

        if (request.Picture is not null)
        {
            // An empty string clears it (e.g. the user removing an auto-provisioned avatar); null leaves it
            // untouched, distinguishing "not part of this update" from "clear it".
            user.Picture = request.Picture.Length == 0 ? null : request.Picture;
        }

        if (request.FirstName is not null || request.LastName is not null || request.Picture is not null)
        {
            await userManager.UpdateAsync(user).ConfigureAwait(false);
        }

        if (!string.IsNullOrEmpty(request.NewPassword))
        {
            if (string.IsNullOrEmpty(request.OldPassword))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
                    { ["oldPassword"] = ["Required to set a new password."] });
            }

            var changeResult = await userManager.ChangePasswordAsync(user, request.OldPassword, request.NewPassword)
                .ConfigureAwait(false);
            if (!changeResult.Succeeded)
            {
                return Results.ValidationProblem(ToErrorDictionary(changeResult));
            }
        }

        if (!string.IsNullOrEmpty(request.NewEmail) &&
            !string.Equals(request.NewEmail, user.Email, StringComparison.OrdinalIgnoreCase))
        {
            user.Email = request.NewEmail;
            user.UserName = request.NewEmail;
            user.EmailConfirmed = false;
            await userManager.UpdateAsync(user).ConfigureAwait(false);

            var userId = await userManager.GetUserIdAsync(user).ConfigureAwait(false);
            var code = await userManager.GenerateEmailConfirmationTokenAsync(user).ConfigureAwait(false);
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));

            // Points at the page Huia provides. If Huia isn't installed, the app is expected to map
            // its own /identity/account/confirmemail-equivalent, or override IEmailSender<HuiaUser> to link
            // elsewhere.
            var baseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
            var link =
                $"{baseUrl}/identity/account/confirmemail?userId={Uri.EscapeDataString(userId)}&code={Uri.EscapeDataString(code)}";
            await emailSender.SendConfirmationLinkAsync(user, user.Email!, link).ConfigureAwait(false);
        }

        return Results.Ok(await BuildInfoResponseAsync(user, userManager).ConfigureAwait(false));
    }
#pragma warning restore MA0051

    /// <summary>
    /// Sends an OTP to <paramref name="request"/>'s <c>NewPhoneNumber</c> via SMS, the first of two steps to
    /// change the signed-in user's phone number (see <see cref="VerifyPhoneChangeAsync"/>) - mirrors
    /// <c>PhoneLoginModel.OnPostAsync</c>'s own validate/rate-limit/send sequence, but against
    /// <see cref="UserManager{TUser}.GenerateChangePhoneNumberTokenAsync"/> instead of the generic two-factor
    /// token pair: that token is bound to the specific phone number (its purpose is
    /// <c>"ChangePhoneNumber:" + phoneNumber</c>), so no pending-state cookie is needed - the caller just
    /// resends the same number at verify time.
    /// </summary>
    private static async Task<IResult> RequestPhoneChangeAsync(RequestPhoneChangeRequest request,
        ClaimsPrincipal principal, UserManager<HuiaUser> userManager, HuiaOptions options,
        IStringLocalizer<HuiaResources> localizer, [FromServices] IPhoneOtpRateLimiter? rateLimiter,
        [FromServices] ISmsSender<HuiaUser>? smsSender, CancellationToken cancellationToken)
    {
        var user = await userManager.GetSignedInUserAsync(principal).ConfigureAwait(false);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var flowError = ValidatePasswordlessFlowEnabled(options, nameof(RequestPhoneChangeRequest.NewPhoneNumber));
        if (flowError is not null)
        {
            return Results.ValidationProblem(flowError);
        }

        var normalized = PhoneNumberValidator.TryNormalizeE164(request.NewPhoneNumber);
        if (normalized is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [nameof(RequestPhoneChangeRequest.NewPhoneNumber)] =
                    ["Enter a valid phone number in international format, e.g. +15551234567."],
            });
        }

        var acquireResult = await rateLimiter!.TryAcquireAsync(normalized, cancellationToken).ConfigureAwait(false);
        if (!acquireResult.IsAcquired)
        {
            return Results.Problem(PhoneOtpCooldownFormatter.FormatMessage(localizer, acquireResult.RetryAfter),
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        var code = await userManager.GenerateChangePhoneNumberTokenAsync(user, normalized).ConfigureAwait(false);
        await smsSender!.SendOtpAsync(user, normalized, code).ConfigureAwait(false);

        return Results.Ok(new RequestPhoneChangeResponse(PhoneNumberMasker.Mask(normalized)));
    }

    /// <summary>
    /// Verifies the OTP <see cref="RequestPhoneChangeAsync"/> sent and, on success, applies the phone number
    /// change - <see cref="UserManager{TUser}.ChangePhoneNumberAsync"/> sets <c>PhoneNumber</c>,
    /// <c>PhoneNumberConfirmed = true</c>, and persists in one call. Deliberately does not touch
    /// <see cref="HuiaUser.PasswordlessLoginEnabled"/> - confirming a phone number here must never implicitly
    /// grant a new sign-in path (see that property's own doc comment).
    /// </summary>
    private static async Task<IResult> VerifyPhoneChangeAsync(VerifyPhoneChangeRequest request,
        ClaimsPrincipal principal, UserManager<HuiaUser> userManager, HuiaOptions options,
        IStringLocalizer<HuiaResources> localizer)
    {
        var user = await userManager.GetSignedInUserAsync(principal).ConfigureAwait(false);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var flowError = ValidatePasswordlessFlowEnabled(options, nameof(VerifyPhoneChangeRequest.NewPhoneNumber));
        if (flowError is not null)
        {
            return Results.ValidationProblem(flowError);
        }

        var normalized = PhoneNumberValidator.TryNormalizeE164(request.NewPhoneNumber);
        if (normalized is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [nameof(VerifyPhoneChangeRequest.NewPhoneNumber)] =
                    ["Enter a valid phone number in international format, e.g. +15551234567."],
            });
        }

        var result = await userManager.ChangePhoneNumberAsync(user, normalized, request.Code).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            await userManager.AccessFailedAsync(user).ConfigureAwait(false);
            if (await userManager.IsLockedOutAsync(user).ConfigureAwait(false))
            {
                return Results.Problem("Too many failed attempts. Try again later.",
                    statusCode: StatusCodes.Status423Locked);
            }

            return Results.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [nameof(VerifyPhoneChangeRequest.Code)] = [localizer["InvalidOrExpiredCodeError"]],
            });
        }

        await userManager.ResetAccessFailedCountAsync(user).ConfigureAwait(false);

        return Results.Ok(await BuildInfoResponseAsync(user, userManager).ConfigureAwait(false));
    }

    /// <summary>
    /// Clears the signed-in user's phone number - no OTP needed, since removing data is inherently lower-risk
    /// than adding/changing it (same reasoning <c>ManageExternalLoginsEndpoints.RemoveExternalLoginAsync</c>
    /// already applies to removing a login), but guarded the same way that method is: if phone-based sign-in
    /// is this account's only credential, removing it would strand the user entirely.
    /// </summary>
    private static async Task<IResult> RemovePhoneAsync(ClaimsPrincipal principal, UserManager<HuiaUser> userManager)
    {
        var user = await userManager.GetSignedInUserAsync(principal).ConfigureAwait(false);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        if (user.PasswordlessLoginEnabled)
        {
            var hasPassword = await userManager.HasPasswordAsync(user).ConfigureAwait(false);
            var logins = await userManager.GetLoginsAsync(user).ConfigureAwait(false);
            if (!hasPassword && logins.Count == 0)
            {
                return Results.Problem("Cannot remove your only sign-in method.",
                    statusCode: StatusCodes.Status400BadRequest);
            }
        }

        if (user.PhoneNumber is not null)
        {
            user.PhoneNumber = null;
            user.NormalizedPhoneNumber = null;
            user.PhoneNumberConfirmed = false;
            await userManager.UpdateAsync(user).ConfigureAwait(false);
        }

        return Results.Ok(await BuildInfoResponseAsync(user, userManager).ConfigureAwait(false));
    }

    /// <summary>Returns a validation-problem dictionary keyed by <paramref name="fieldName"/> if the app
    /// doesn't have passwordless phone sign-in enabled, or <see langword="null"/> if it does - phone changes
    /// only make sense for an app that can actually use a phone number to sign in (see
    /// <c>UsersEndpoints.ValidateIdentifierChoice</c> for the same gating on admin-created accounts). Checked
    /// before <see cref="IPhoneOtpRateLimiter"/>/<see cref="ISmsSender{TUser}"/> are touched - both are only
    /// registered in DI when this flow is enabled.</summary>
    internal static Dictionary<string, string[]>? ValidatePasswordlessFlowEnabled(HuiaOptions options,
        string fieldName)
        => options.Authentication.PasswordlessFlowEnabled
            ? null
            : new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [fieldName] = ["Phone-based sign-in isn't enabled for this app."],
            };

    private static async Task<InfoResponse> BuildInfoResponseAsync(HuiaUser user, UserManager<HuiaUser> userManager)
        => new(user.Email, await userManager.IsEmailConfirmedAsync(user).ConfigureAwait(false), user.PhoneNumber,
            await userManager.IsPhoneNumberConfirmedAsync(user).ConfigureAwait(false), user.FirstName, user.LastName,
            user.Picture);

    private static Dictionary<string, string[]> ToErrorDictionary(IdentityResult result)
        => result.Errors
            .GroupBy(e => e.Code, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray(), StringComparer.Ordinal);

    private sealed record InfoResponse(
        string? Email, bool IsEmailConfirmed, string? PhoneNumber, bool IsPhoneNumberConfirmed, string? FirstName,
        string? LastName, string? Picture);

    private sealed record UpdateInfoRequest(
        string? NewEmail,
        string? NewPassword,
        string? OldPassword,
        string? FirstName,
        string? LastName,
        string? Picture);

    private sealed record RequestPhoneChangeRequest(string NewPhoneNumber);

    private sealed record RequestPhoneChangeResponse(string MaskedPhoneNumber);

    private sealed record VerifyPhoneChangeRequest(string NewPhoneNumber, string Code);
}