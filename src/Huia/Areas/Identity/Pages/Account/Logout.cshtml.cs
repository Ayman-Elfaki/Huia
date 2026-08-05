using System.Security.Claims;
using Huia.Endpoints.Connect;
using Huia.Common;
using Huia.Identity;
using Huia.Sessions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;

namespace Huia.Areas.Identity.Pages.Account;

/// <summary>Signs the current user out.</summary>
public class LogoutModel(
    SignInManager<HuiaUser> signInManager,
    UserManager<HuiaUser> userManager,
    LogoutNotifier logoutNotifier,
    UserSessionService sessions,
    IOpenIddictApplicationManager applicationManager,
    ILogger<LogoutModel> logger) : PageModel
{
    /// <summary>
    /// Clears the sign-in cookie, ends this session (back-channel-notifying any other client tied to it —
    /// see <see cref="LogoutNotifier"/>), and redirects to <paramref name="returnUrl"/> or the login page.
    /// </summary>
    /// <remarks>
    /// Unlike <c>/connect/logout</c> (<c>Huia.Endpoints.Connect.EndSessionEndpoints</c>), this page can't
    /// front-channel-log-out a relying party — that needs the browser to actually load an iframe onto each
    /// one, which requires returning an intermediate HTML page instead of a plain redirect, something this
    /// Razor Page's own navigation (to <paramref name="returnUrl"/> or the login page) doesn't leave room
    /// for. A front-channel-only relying party won't hear about a sign-out that happens here.
    /// </remarks>
    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        var result = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var sessionId = result is { Succeeded: true } ? result.Principal!.FindFirstValue(SessionClaimTypes.Sid) : null;

        await signInManager.SignOutAsync();
        logger.LogInformation("User signed out.");

        if (sessionId is not null)
        {
            var user = await userManager.GetUserAsync(result.Principal!);
            if (user is not null)
            {
                var subject = await userManager.GetUserIdAsync(user);
                var targets = await logoutNotifier.CollectAsync(subject, sessionId, excludeClientId: null, HttpContext.RequestAborted);
                await logoutNotifier.NotifyBackChannelAsync(sessionId, targets.BackChannelLogoutTargets, HttpContext.RequestAborted);
            }

            await sessions.RevokeAsync(sessionId, HttpContext.RequestAborted);
        }

        return returnUrl is not null
            ? Redirect(await ReturnUrlValidator.ResolveAsync(Request, returnUrl, applicationManager))
            : RedirectToPage("./Login");
    }
}
