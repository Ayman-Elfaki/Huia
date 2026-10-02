using System.Security.Claims;
using Huia.OpenId.Flows;
using Huia.OpenId.UI;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Huia.OpenId.EntityFrameworkCore.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using OpenIddict.Client;
using OpenIddict.Client.AspNetCore;

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
    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        var externalIdp = User.FindFirstValue(HuiaConstants.ClaimTypes.ExternalIdp);
        var externalIdToken = User.FindFirstValue(HuiaConstants.ClaimTypes.ExternalIdToken);

        await signInManager.SignOutAsync();

        string targetUrl;
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            targetUrl = returnUrl;
        }
        else if (TenantClientHome.Resolve(Tenant) is { } clientHome)
        {
            targetUrl = clientHome;
        }
        else
        {
            var pathBase = Request.PathBase.HasValue ? Request.PathBase.Value! : "/";
            targetUrl = pathBase.EndsWith('/') ? pathBase : pathBase + "/";
        }

        if (externalIdp is not null)
        {
            var clientService = HttpContext.RequestServices.GetService<OpenIddictClientService>();
            if (clientService is not null)
            {
                try
                {
                    var configuration = await clientService.GetServerConfigurationByRegistrationIdAsync(externalIdp);
                    if (configuration?.EndSessionEndpoint is not null)
                    {
                        var properties = new AuthenticationProperties { RedirectUri = targetUrl };
                        properties.Items[OpenIddictClientAspNetCoreConstants.Properties.RegistrationId] = externalIdp;
                        if (externalIdToken is not null)
                        {
                            properties.Items[OpenIddictClientAspNetCoreConstants.Properties.IdentityTokenHint] = externalIdToken;
                        }

                        return SignOut(properties, OpenIddictClientAspNetCoreDefaults.AuthenticationScheme);
                    }
                }
                catch
                {
                    // Fall back to targetUrl
                }
            }
        }

        if (Url.IsLocalUrl(targetUrl))
        {
            return LocalRedirect(targetUrl);
        }

        return Redirect(targetUrl);
    }
}
