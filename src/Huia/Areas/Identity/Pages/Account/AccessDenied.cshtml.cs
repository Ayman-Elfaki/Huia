using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Huia.Areas.Identity.Pages.Account;

/// <summary>Shown when a signed-in user is denied access to a resource.</summary>
[AllowAnonymous]
public class AccessDeniedModel : PageModel
{
    /// <summary>
    /// Where to send the user once they sign in as a different (authorized) account — the cookie
    /// authentication handler's own <c>AccessDeniedPath</c> redirect appends this (as <c>?ReturnUrl=</c>,
    /// bound here case-insensitively) automatically, carrying through whatever the user was actually trying
    /// to reach, including an in-progress OAuth flow if that's what triggered the access-denied response.
    /// </summary>
    public string? ReturnUrl { get; set; }

    /// <summary>Renders the page.</summary>
    public void OnGet(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
    }
}
