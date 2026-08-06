using System.Collections.Immutable;
using System.Security.Claims;
using Huia.Common;
using Huia.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;

namespace Huia.Areas.Identity.Pages.Account;

/// <summary>
/// The bundled browser UI for approving or denying a pending device authorization code — see
/// <see cref="DeviceVerifier"/>'s doc comment for how this relates to the
/// JSON <c>/connect/verify</c> endpoints. Unlike those (gated by <c>RequireAuthorization</c>, which 401s an
/// unauthenticated caller by design — see <c>ServiceCollectionExtensions</c>'
/// <c>ConfigureApplicationCookie</c>'s <c>OnRedirectToLogin</c>), this page checks the sign-in cookie itself
/// and redirects to <c>Login</c> like any other page here, since a real user landing on this link from their
/// phone/browser needs a normal sign-in redirect, not a bare 401.
/// </summary>
/// <remarks>
/// <see cref="IgnoreAntiforgeryTokenAttribute"/> disables the automatic antiforgery check <c>AddRazorPages()</c>
/// otherwise applies to every POST: that check runs as an early MVC filter, before this page's own handler —
/// let alone <see cref="RedirectToSignInIfNeededAsync"/>'s <c>HttpContext.User</c> reassignment — ever gets a
/// chance to run. Since Huia's *default* authentication scheme is the OpenIddict bearer validator (never
/// satisfied by a plain browser request), <c>HttpContext.User</c> at that point is still anonymous even
/// though the form was rendered for (and its antiforgery token bound to) a signed-in user — so the automatic
/// check fails every time with "the provided antiforgery token was meant for a different claims-based user
/// than the current user". Validating it manually in <see cref="OnPostAsync"/>, after that reassignment,
/// avoids the ordering problem entirely.
/// </remarks>
[AllowAnonymous]
[IgnoreAntiforgeryToken]
public class DeviceModel(
    UserManager<HuiaUser> userManager,
    IOpenIddictApplicationManager applicationManager,
    IOpenIddictScopeManager scopeManager,
    IAntiforgery antiforgery) : PageModel
{
    /// <summary>
    /// The code from the device's own screen. Bound to the "user_code" query/form key specifically (not the
    /// C# property name) because that's the exact key both OpenIddict's own end-user-verification handling
    /// (GET query string, POST form field) and <c>verification_uri_complete</c> use — keeping one name across
    /// all three means a scanned QR code or clicked link, this page's own model binding, and OpenIddict's
    /// parallel raw-form read all agree without any translation.
    /// </summary>
    [BindProperty(Name = "user_code", SupportsGet = true)]
    public string? UserCode { get; set; }

    /// <summary>The application name to show once <see cref="UserCode"/> resolves to a pending device request.</summary>
    public string? ApplicationName { get; private set; }

    /// <summary>The scopes the device is requesting.</summary>
    public ImmutableArray<string> Scopes { get; private set; } = [];

    /// <summary>Whether <see cref="UserCode"/> didn't resolve to a live, pending device request.</summary>
    public bool CodeInvalid { get; private set; }

    /// <summary>Set once a decision has been submitted — <see langword="true"/> approved, <see langword="false"/> denied.</summary>
    public bool? Decision { get; private set; }

    /// <summary>
    /// Renders the "enter your code" form if <see cref="UserCode"/> is empty, or looks it up and shows the
    /// requesting application/scopes otherwise (redirecting to sign in first if needed).
    /// </summary>
    public async Task<IActionResult> OnGetAsync()
    {
        if (string.IsNullOrWhiteSpace(UserCode))
        {
            return Page();
        }

        if (await RedirectToSignInIfNeededAsync() is { } redirect)
        {
            return redirect;
        }

        await DescribeAsync().ConfigureAwait(false);
        return Page();
    }

    /// <summary>Approves or denies the pending device code named by <see cref="UserCode"/>.</summary>
    public async Task<IActionResult> OnPostAsync(bool accept)
    {
        if (string.IsNullOrWhiteSpace(UserCode))
        {
            return RedirectToPage();
        }

        if (await RedirectToSignInIfNeededAsync() is { } redirect)
        {
            return redirect;
        }

        try
        {
            await antiforgery.ValidateRequestAsync(HttpContext).ConfigureAwait(false);
        }
        catch (AntiforgeryValidationException)
        {
            return BadRequest();
        }

        var request = await DescribeAsync().ConfigureAwait(false);
        if (request is null)
        {
            return Page();
        }

        if (!accept)
        {
            // Marks the device code denied server-side (OpenIddict's own authentication handler for this
            // scheme intercepts Forbid to do so) — without this, a poller would just see
            // authorization_pending until the code naturally expires, never access_denied, even though this
            // page already told the user their decision was recorded. ForbidAsync sets a 403 as a side
            // effect (the entire point of it for the JSON API in DeviceEndpoints, which returns that as-is)
            // but this page still wants to render its own "denied" card afterward, so the status gets reset
            // to 200 first — nothing else about the response (body/headers) has been written yet at this
            // point, so this is safe.
            await HttpContext.ForbidAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)
                .ConfigureAwait(false);
            HttpContext.Response.StatusCode = StatusCodes.Status200OK;

            Decision = false;
            return Page();
        }

        var user = await userManager.GetUserAsync(HttpContext.User).ConfigureAwait(false);
        if (user is null)
        {
            return Unauthorized();
        }

        var identity = await DeviceVerifier
            .ApproveAsync(user, request.Scopes, userManager, scopeManager)
            .ConfigureAwait(false);
        await HttpContext.SignInAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity)).ConfigureAwait(false);

        Decision = true;
        return Page();
    }

    private async Task<IActionResult?> RedirectToSignInIfNeededAsync()
    {
        var signedIn = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme).ConfigureAwait(false);
        if (!signedIn.Succeeded)
        {
            return RedirectToPage("./Login", new { returnUrl = Url.Page("./Device", new { user_code = UserCode }) });
        }

        // This page is [AllowAnonymous] (see class remarks), so — unlike DeviceEndpoints' route-level
        // RequireAuthorization(...AddAuthenticationSchemes(IdentityConstants.ApplicationScheme)...), whose
        // authorization middleware sets HttpContext.User to its result as a side effect — a plain manual
        // AuthenticateAsync call here doesn't touch HttpContext.User at all; it stays whatever the app's
        // *default* scheme (OpenIddict's bearer validator, never satisfied by a browser request with no
        // Authorization header) left it as. GetUserAsync below reads HttpContext.User directly, so it has
        // to be set explicitly here.
        HttpContext.User = signedIn.Principal!;
        return null;
    }

    private async Task<DeviceVerificationRequest?> DescribeAsync()
    {
        var result = await DeviceVerifier.AuthenticateAsync(HttpContext).ConfigureAwait(false);
        var request = await DeviceVerifier.TryDescribeAsync(result, applicationManager).ConfigureAwait(false);
        if (request is null)
        {
            CodeInvalid = true;
            return null;
        }

        ApplicationName = request.ApplicationName;
        Scopes = request.Scopes;
        return request;
    }
}