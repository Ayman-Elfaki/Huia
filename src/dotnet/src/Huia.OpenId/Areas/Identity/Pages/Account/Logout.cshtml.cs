using Huia.OpenId.Flows;
using Huia.OpenId.UI;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace Huia.OpenId.Areas.Identity.Pages.Account;

/// <summary>Ends the interactive session cookie. The OIDC end-session endpoint is separate (<c>/connect/logout</c>).</summary>
public sealed class LogoutModel(SignInManager<HuiaUser> signInManager, IStringLocalizer<SharedResource> localizer) : HuiaAccountPageModel
{
    /// <summary>Handles the GET (renders the confirmation button).</summary>
    public void OnGet()
    {
        ViewData["Title"] = localizer["Logout.Title"].Value;
        ViewData["Heading"] = localizer["Logout.Heading"].Value;
    }

    /// <summary>Signs out and returns to the tenant's client application (else the tenant root).</summary>
    /// <returns>A redirect to the client home, or the tenant root when the tenant has no client.</returns>
    public async Task<IActionResult> OnPostAsync()
    {
        await signInManager.SignOutAsync();

        if (TenantClientHome.Resolve(Tenant) is { } clientHome)
        {
            return Redirect(clientHome);
        }

        var pathBase = Request.PathBase.HasValue ? Request.PathBase.Value! : "/";
        return LocalRedirect(pathBase.EndsWith('/') ? pathBase : pathBase + "/");
    }
}
