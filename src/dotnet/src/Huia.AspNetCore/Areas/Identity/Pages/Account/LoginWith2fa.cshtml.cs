using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Finbuckle.MultiTenant.Abstractions;
using Huia.AspNetCore.Flows;
using Huia.AspNetCore.Identity;
using Huia.AspNetCore.UI;
using Huia.EntityFrameworkCore.Multitenancy;
using Huia.Events;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace Huia.AspNetCore.Areas.Identity.Pages.Account;

/// <summary>
/// Second step of a password sign-in for an account that requires a passkey as its second factor. The
/// passkey assertion itself runs entirely against the <c>identity/account/passkey/2fa*</c> endpoints
/// (driven by <c>passkey.js</c>); this page also offers a recovery code as the fallback.
/// </summary>
public sealed class LoginWith2faModel(
    IHuiaFlowIdentityFactory flowIdentity,
    IReturnUrlProtector returnUrlProtector,
    IMultiTenantContextAccessor tenantAccessor,
    IHuiaEventPublisher events,
    IStringLocalizer<SharedResource> localizer,
    TimeProvider timeProvider) : HuiaAccountPageModel
{
    /// <summary>The bound recovery code.</summary>
    [BindProperty]
    public InputModel Input { get; set; } = new();

    /// <summary>The opaque flow token round-tripped through the form (pending user id + return URL).</summary>
    [BindProperty(SupportsGet = true)]
    public string Flow { get; set; } = string.Empty;

    /// <summary>Whether the earlier password step asked to be remembered.</summary>
    [BindProperty(SupportsGet = true)]
    public bool RememberMe { get; set; }

    /// <summary>Handles the GET.</summary>
    /// <returns>The page, or 404 when the token is invalid or passkey 2FA is not available.</returns>
    public async Task<IActionResult> OnGetAsync()
    {
        SetHeadings();
        return await ResolveUserAsync() is null ? NotFound() : Page();
    }

    /// <summary>Redeems a two-factor recovery code and completes the sign-in.</summary>
    /// <returns>A redirect on success, otherwise the page with an error.</returns>
    public async Task<IActionResult> OnPostRecoveryCodeAsync()
    {
        SetHeadings();

        var user = await ResolveUserAsync();
        if (user is null)
        {
            return NotFound();
        }

        var code = (Input.RecoveryCode ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal);
        if (code.Length == 0)
        {
            ErrorMessage = localizer["LoginWith2fa.RecoveryInvalid"].Value;
            return Page();
        }

        var identity = flowIdentity.Create(HuiaAuthFlow.Passkey);
        var redeem = await identity.UserManager.RedeemTwoFactorRecoveryCodeAsync(user, code);
        if (!redeem.Succeeded)
        {
            ErrorMessage = localizer["LoginWith2fa.RecoveryInvalid"].Value;
            return Page();
        }

        await HttpContext.SignOutAsync(IdentityConstants.TwoFactorUserIdScheme);
        await identity.SignInManager.SignInWithClaimsAsync(user, RememberMe,
        [
            new Claim(HuiaConstants.ClaimTypes.AuthenticationMethod, HuiaConstants.AuthenticationMethods.Password),
            new Claim(HuiaConstants.ClaimTypes.AuthenticationMethod, HuiaConstants.AuthenticationMethods.MultiFactor),
        ]);

        var tenantId = tenantAccessor.RequireCurrentTenantId();
        await events.PublishAsync(new UserLoggedInEvent(
            tenantId, user.Id, HuiaConstants.AuthenticationMethods.MultiFactor, null, timeProvider.GetUtcNow()));

        var state = returnUrlProtector.Read(Flow);
        return ResolvePostAuthRedirect(returnUrlProtector.SanitizeReturnUrl(state?.ReturnUrl, HttpContext));
    }

    private async Task<EntityFrameworkCore.Entities.HuiaUser?> ResolveUserAsync()
    {
        if (!IsPasskeySecondFactorAllowed)
        {
            return null;
        }

        var state = returnUrlProtector.Read(Flow);
        if (state?.UserId is not { } userId)
        {
            return null;
        }

        var user = await flowIdentity.Create(HuiaAuthFlow.Passkey).UserManager.FindByIdAsync(userId);
        return user is { TwoFactorEnabled: true } ? user : null;
    }

    private void SetHeadings()
    {
        ViewData["Title"] = localizer["LoginWith2fa.Title"].Value;
        ViewData["Heading"] = localizer["LoginWith2fa.Heading"].Value;
    }

    /// <summary>The recovery-code form field.</summary>
    public sealed class InputModel
    {
        /// <summary>A two-factor recovery code.</summary>
        [Required]
        public string? RecoveryCode { get; set; }
    }
}
