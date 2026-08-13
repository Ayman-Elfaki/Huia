using System.Security.Claims;
using System.Text;
using Huia.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;

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

        return group;
    }

    private static async Task<IResult> GetInfoAsync(ClaimsPrincipal principal, UserManager<HuiaUser> userManager)
    {
        var user = await userManager.GetSignedInUserAsync(principal).ConfigureAwait(false);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(new InfoResponse(user.Email!,
            await userManager.IsEmailConfirmedAsync(user).ConfigureAwait(false), user.FirstName, user.LastName,
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

            // Points at the page Huia.UI provides. If Huia.UI isn't installed, the app is expected to map
            // its own /identity/account/confirmemail-equivalent, or override IEmailSender<HuiaUser> to link
            // elsewhere.
            var baseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
            var link =
                $"{baseUrl}/identity/account/confirmemail?userId={Uri.EscapeDataString(userId)}&code={Uri.EscapeDataString(code)}";
            await emailSender.SendConfirmationLinkAsync(user, user.Email!, link).ConfigureAwait(false);
        }

        return Results.Ok(new InfoResponse(user.Email!,
            await userManager.IsEmailConfirmedAsync(user).ConfigureAwait(false), user.FirstName, user.LastName,
            user.Picture));
    }
#pragma warning restore MA0051

    private static Dictionary<string, string[]> ToErrorDictionary(IdentityResult result)
        => result.Errors
            .GroupBy(e => e.Code, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray(), StringComparer.Ordinal);


    private sealed record InfoResponse(
        string Email, bool IsEmailConfirmed, string? FirstName, string? LastName, string? Picture);

    private sealed record UpdateInfoRequest(
        string? NewEmail,
        string? NewPassword,
        string? OldPassword,
        string? FirstName,
        string? LastName,
        string? Picture);
}