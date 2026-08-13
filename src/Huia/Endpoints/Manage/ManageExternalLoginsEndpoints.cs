using System.Security.Claims;
using Huia.Common;
using Huia.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using OpenIddict.Abstractions;

namespace Huia.Endpoints.Manage;

/// <summary>
/// Endpoints for a signed-in user to view, link, and unlink external (third-party) sign-in providers on
/// their own account. Listing and unlinking are plain JSON, like the rest of <c>Manage/</c> — but starting a
/// link needs a real browser redirect through the provider (the same reason sign-in/register are Razor Pages
/// rather than JSON, see this group's own doc comment on <c>MapHuiaManageEndpoints</c>), so
/// <c>link</c>/<c>link-callback</c> return a challenge/redirect instead of JSON.
/// </summary>
internal static class ManageExternalLoginsEndpoints
{
    public static RouteGroupBuilder MapManageExternalLoginsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/external-logins");

        group.MapGet("", GetExternalLoginsAsync);
        group.MapDelete("/{provider}/{providerKey}", RemoveExternalLoginAsync);
        group.MapGet("/{provider}/link", LinkAsync);
        group.MapGet("/{provider}/link-callback", LinkCallbackAsync);

        return group;
    }

    private static async Task<IResult> GetExternalLoginsAsync(ClaimsPrincipal principal,
        UserManager<HuiaUser> userManager)
    {
        var user = await userManager.GetSignedInUserAsync(principal).ConfigureAwait(false);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var logins = await userManager.GetLoginsAsync(user).ConfigureAwait(false);

        return Results.Ok(new ExternalLoginsResponse(
            [.. logins.Select(l => new ExternalLoginResponse(l.LoginProvider, l.ProviderDisplayName, l.ProviderKey))],
            await userManager.HasPasswordAsync(user).ConfigureAwait(false)));
    }

    private static async Task<IResult> RemoveExternalLoginAsync(string provider, string providerKey,
        ClaimsPrincipal principal, UserManager<HuiaUser> userManager)
    {
        var user = await userManager.GetSignedInUserAsync(principal).ConfigureAwait(false);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var logins = await userManager.GetLoginsAsync(user).ConfigureAwait(false);
        var hasPassword = await userManager.HasPasswordAsync(user).ConfigureAwait(false);

        if (!hasPassword && logins.Count <= 1)
        {
            return Results.Problem("Cannot remove your only sign-in method.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await userManager.RemoveLoginAsync(user, provider, providerKey).ConfigureAwait(false);

        return result.Succeeded
            ? Results.Ok()
            : Results.ValidationProblem(result.Errors.GroupBy(e => e.Code, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray(), StringComparer.Ordinal));
    }

    /// <summary>
    /// Challenges <paramref name="provider"/> to link it to the currently signed-in user, resuming at
    /// <see cref="LinkCallbackAsync"/>. The signed-in user's id is stashed as the challenge's xsrf key
    /// (<c>ConfigureExternalAuthenticationProperties</c>'s third parameter) and re-checked on the way back
    /// (<see cref="LinkCallbackAsync"/>'s <c>expectedXsrfKey</c>) — without it, an attacker who starts their
    /// own link flow and gets a victim to open the resulting callback URL could bind their external identity
    /// to the victim's account instead of their own.
    /// </summary>
    private static async Task<IResult> LinkAsync(string provider, string? returnUrl, ClaimsPrincipal principal,
        HttpContext httpContext, UserManager<HuiaUser> userManager, SignInManager<HuiaUser> signInManager)
    {
        var userId = await userManager.GetUserAsync(principal).ConfigureAwait(false) is { } user
            ? await userManager.GetUserIdAsync(user).ConfigureAwait(false)
            : null;
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        // Clears any stale pending external-sign-in state left over from an unrelated, abandoned attempt.
        await httpContext.SignOutAsync(IdentityConstants.ExternalScheme).ConfigureAwait(false);

        var redirectUrl =
            $"/api/identity/manage/external-logins/{Uri.EscapeDataString(provider)}/link-callback?returnUrl={Uri.EscapeDataString(returnUrl ?? string.Empty)}";
        var properties = signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl, userId);

        return Results.Challenge(properties, [provider]);
    }

    private static async Task<IResult> LinkCallbackAsync(string provider, string? returnUrl,
        ClaimsPrincipal principal, UserManager<HuiaUser> userManager, SignInManager<HuiaUser> signInManager,
        IOpenIddictApplicationManager applicationManager, HttpRequest request)
    {
        var user = await userManager.GetUserAsync(principal).ConfigureAwait(false);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var userId = await userManager.GetUserIdAsync(user).ConfigureAwait(false);
        var info = await signInManager.GetExternalLoginInfoAsync(userId).ConfigureAwait(false);
        if (info is null || !string.Equals(info.LoginProvider, provider, StringComparison.Ordinal))
        {
            return Results.Problem("Error loading external login information for linking.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await userManager.AddLoginAsync(user, info).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return Results.ValidationProblem(result.Errors.GroupBy(e => e.Code, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray(), StringComparer.Ordinal));
        }

        await signInManager.UpdateExternalAuthenticationTokensAsync(info).ConfigureAwait(false);

        return Results.Redirect(await ReturnUrlValidator.ResolveAsync(request, returnUrl, applicationManager)
            .ConfigureAwait(false));
    }

    private sealed record ExternalLoginResponse(string LoginProvider, string? ProviderDisplayName, string ProviderKey);

    private sealed record ExternalLoginsResponse(IReadOnlyList<ExternalLoginResponse> Logins, bool HasPassword);
}
