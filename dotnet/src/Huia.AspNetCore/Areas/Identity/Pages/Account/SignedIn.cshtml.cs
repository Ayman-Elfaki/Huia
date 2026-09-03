using Huia.AspNetCore.Flows;
using Huia.AspNetCore.UI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace Huia.AspNetCore.Areas.Identity.Pages.Account;

/// <summary>
/// A plain confirmation shown when an authenticated user lands on the tenant root with nowhere else to
/// go — for example after signing in directly at the identity server (no client application in the
/// flow). <c>MapHuiaHome</c> redirects here instead of bouncing back to itself.
/// </summary>
public sealed class SignedInModel(IStringLocalizer<SharedResource> localizer) : HuiaAccountPageModel
{
    /// <summary>The signed-in user's display name, when the cookie carries one.</summary>
    public string? UserName { get; private set; }

    /// <summary>The tenant's client application home, when one is registered. Offered as "continue" link.</summary>
    public string? ClientHomeUrl { get; private set; }

    /// <summary>Handles the GET. Sends an anonymous caller to sign-in.</summary>
    /// <returns>The page, or a redirect to sign-in.</returns>
    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Redirect($"{PathBase}/identity/account/login");
        }

        ViewData["Title"] = localizer["SignedIn.Title"].Value;
        ViewData["Heading"] = localizer["SignedIn.Heading"].Value;
        UserName = User.Identity.Name;
        ClientHomeUrl = TenantClientHome.Resolve(Tenant);
        return Page();
    }
}
