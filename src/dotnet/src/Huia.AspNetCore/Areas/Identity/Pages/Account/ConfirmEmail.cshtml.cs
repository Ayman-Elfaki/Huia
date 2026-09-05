using System.Text;
using Huia.AspNetCore.Flows;
using Huia.AspNetCore.Identity;
using Huia.AspNetCore.UI;
using Huia.EntityFrameworkCore.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Localization;

namespace Huia.AspNetCore.Areas.Identity.Pages.Account;

/// <summary>Consumes an email-confirmation link.</summary>
public sealed class ConfirmEmailModel(IHuiaFlowIdentityFactory flowIdentity, IStringLocalizer<SharedResource> localizer) : HuiaAccountPageModel
{
    /// <summary>Whether the confirmation succeeded.</summary>
    public bool Confirmed { get; private set; }

    /// <summary>The tenant's client application home, when one is registered. Offered instead of a
    /// sign-in link, since the link is reached outside a valid OAuth flow.</summary>
    public string? ClientHomeUrl { get; private set; }

    /// <summary>Handles the GET.</summary>
    /// <param name="userId">The user id from the link.</param>
    /// <param name="code">The Base64Url-encoded confirmation token.</param>
    /// <returns>The page.</returns>
    public async Task<IActionResult> OnGetAsync(string? userId, string? code)
    {
        ViewData["Title"] = localizer["RegisterConfirmation.Title"].Value;
        ViewData["Heading"] = localizer["RegisterConfirmation.Heading"].Value;
        ClientHomeUrl = TenantClientHome.Resolve(Tenant);

        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(code))
        {
            return NotFound();
        }

        var userManager = flowIdentity.Create(HuiaAuthFlow.EmailAndPasswordLogin).UserManager;
        var user = await userManager.FindByIdAsync(userId);
        if (user is not null)
        {
            var token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
            Confirmed = (await userManager.ConfirmEmailAsync(user, token)).Succeeded;
        }

        return Page();
    }
}
