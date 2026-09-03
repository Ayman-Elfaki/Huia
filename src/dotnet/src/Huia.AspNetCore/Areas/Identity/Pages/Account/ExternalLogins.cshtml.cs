using Huia.AspNetCore.Identity;
using Huia.AspNetCore.UI;
using Huia.EntityFrameworkCore.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace Huia.AspNetCore.Areas.Identity.Pages.Account;

/// <summary>
/// Lets a signed-in user link or unlink external sign-in providers. "Add" starts an OpenIddict-client
/// challenge (via <c>identity/account/external/{provider}</c>); the dispatcher detects the live session
/// and links the returned login to this account.
/// </summary>
public sealed class ExternalLoginsModel(
    HuiaUserManager userManager,
    SignInManager<HuiaUser> signInManager,
    IStringLocalizer<SharedResource> localizer) : HuiaAccountPageModel
{
    /// <summary>The providers currently linked to the account.</summary>
    public IList<LinkedProvider> CurrentLogins { get; private set; } = [];

    /// <summary>Configured providers that are not linked yet.</summary>
    public IReadOnlyList<Options.ExternalProviderRegistration> AvailableProviders { get; private set; } = [];

    /// <summary>Whether any linked provider may be removed (there must be another way to sign in).</summary>
    public bool CanRemove { get; private set; }

    /// <summary>A status line rendered after an add/remove round-trip.</summary>
    public string? StatusMessage { get; private set; }

    /// <summary>The absolute-local path used as the return target of the "add" challenge.</summary>
    public string SelfPath => $"{PathBase}/identity/account/externallogins";

    /// <summary>Handles the GET.</summary>
    /// <param name="linked">Set by the dispatcher after an add round-trip (<c>ok</c> / <c>dupe</c> / <c>error</c>).</param>
    /// <returns>The page, or a redirect to sign in.</returns>
    public async Task<IActionResult> OnGetAsync(string? linked)
    {
        SetHeadings();

        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return RedirectToPage("./Login");
        }

        StatusMessage = linked switch
        {
            "ok" => localizer["ExternalLogins.LinkedOk"].Value,
            "dupe" => localizer["ExternalLogins.LinkedDuplicate"].Value,
            "error" => localizer["ExternalLogins.LinkedError"].Value,
            _ => null,
        };

        await LoadAsync(user);
        return Page();
    }

    /// <summary>Removes a linked provider.</summary>
    /// <param name="loginProvider">The registration id (<c>{tenant}:{provider}</c>).</param>
    /// <param name="providerKey">The upstream subject.</param>
    /// <returns>The page with the outcome.</returns>
    public async Task<IActionResult> OnPostRemoveAsync(string loginProvider, string providerKey)
    {
        SetHeadings();

        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return RedirectToPage("./Login");
        }

        if (!await userManager.CanRemoveExternalLoginAsync(user))
        {
            ErrorMessage = localizer["ExternalLogins.CannotRemoveLast"].Value;
            await LoadAsync(user);
            return Page();
        }

        var result = await userManager.RemoveLoginAsync(user, loginProvider, providerKey);
        if (result.Succeeded)
        {
            await signInManager.RefreshSignInAsync(user);
        }
        else
        {
            ErrorMessage = string.Join(" ", result.Errors.Select(e => e.Description));
        }

        await LoadAsync(user);
        return Page();
    }


    private async Task LoadAsync(HuiaUser user)
    {
        var logins = await userManager.GetLoginsAsync(user);
        CurrentLogins = logins
            .Select(l => new LinkedProvider(l.LoginProvider, l.ProviderKey, ShortName(l.LoginProvider), l.ProviderDisplayName))
            .ToList();

        var linkedIds = logins.Select(l => l.LoginProvider).ToHashSet(StringComparer.Ordinal);
        AvailableProviders = ExternalProviders
            .Where(p => !linkedIds.Contains($"{TenantId}:{p.Name}"))
            .ToList();

        CanRemove = await userManager.CanRemoveExternalLoginAsync(user);
    }

    private void SetHeadings()
    {
        ViewData["Title"] = localizer["ExternalLogins.Title"].Value;
        ViewData["Heading"] = localizer["ExternalLogins.Heading"].Value;
    }

    private static string ShortName(string loginProvider) =>
        loginProvider.Contains(':', StringComparison.Ordinal)
            ? loginProvider[(loginProvider.LastIndexOf(':') + 1)..]
            : loginProvider;

    /// <summary>One linked provider row.</summary>
    /// <param name="LoginProvider">The registration id.</param>
    /// <param name="ProviderKey">The upstream subject.</param>
    /// <param name="ShortName">The provider name without the tenant prefix.</param>
    /// <param name="DisplayName">A friendly name, when the provider supplied one.</param>
    public sealed record LinkedProvider(string LoginProvider, string ProviderKey, string ShortName, string? DisplayName);
}
