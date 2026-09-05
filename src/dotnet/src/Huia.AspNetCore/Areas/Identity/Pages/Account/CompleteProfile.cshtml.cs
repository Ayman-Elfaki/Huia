using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Huia.AspNetCore.Flows;
using Huia.AspNetCore.Identity;
using Huia.AspNetCore.Services;
using Huia.AspNetCore.UI;
using Huia.EntityFrameworkCore.Entities;
using Huia.Events;
using Finbuckle.MultiTenant.Abstractions;
using Huia.EntityFrameworkCore.Multitenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace Huia.AspNetCore.Areas.Identity.Pages.Account;

/// <summary>
/// Final step for flows that resolved to an incomplete account: a phone auto-provisioning signup, an
/// existing blank-name user, or an external sign-up. The <see cref="HuiaUser"/> is created (or updated)
/// here — never with blank names at request time.
/// </summary>
public sealed class CompleteProfileModel(
    IHuiaFlowIdentityFactory flowIdentity,
    IPendingPhoneSignup pendingSignups,
    IMultiTenantContextAccessor tenantAccessor,
    IReturnUrlProtector returnUrlProtector,
    IHuiaEventPublisher events,
    IStringLocalizer<SharedResource> localizer,
    TimeProvider timeProvider) : HuiaAccountPageModel
{
    /// <summary>The bound name fields.</summary>
    [BindProperty]
    public InputModel Input { get; set; } = new();

    /// <summary>The opaque flow token round-tripped through the form.</summary>
    [BindProperty]
    public string Flow { get; set; } = string.Empty;

    /// <summary>Handles the GET.</summary>
    /// <param name="flow">The flow token.</param>
    /// <returns>The page, or 404 when the token is missing or carries nothing to complete.</returns>
    public IActionResult OnGet(string? flow)
    {
        SetHeadings();
        Flow = flow ?? string.Empty;
        var state = returnUrlProtector.Read(Flow);
        if (state is null || (state.PendingSignupId is null && state.UserId is null && state.ExternalProvider is null))
        {
            return NotFound();
        }

        Input = new InputModel { FirstName = state.FirstName ?? string.Empty, LastName = state.LastName ?? string.Empty };
        return Page();
    }

    /// <summary>Handles the POST that creates or completes the account and signs the user in.</summary>
    /// <returns>A redirect to the return URL on success.</returns>
    public async Task<IActionResult> OnPostAsync()
    {
        SetHeadings();
        var state = returnUrlProtector.Read(Flow);
        if (state is null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var tenantId = tenantAccessor.RequireCurrentTenantId();
        var returnUrl = returnUrlProtector.SanitizeReturnUrl(state.ReturnUrl, HttpContext);

        // The completing account belongs to the flow that produced it: an external sign-up runs the
        // external flow, everything else is a phone sign-up.
        var flow = flowIdentity.Create(state.ExternalProvider is null ? HuiaAuthFlow.PhoneLogin : HuiaAuthFlow.ExternalLogin);
        var userManager = flow.UserManager;

        var user = state switch
        {
            { PendingSignupId: { } pendingId } => await CompletePhoneSignupAsync(userManager, tenantId, pendingId, state),
            { UserId: { } userId } => await CompleteExistingUserAsync(userManager, userId),
            { ExternalProvider: { } } => await CompleteExternalSignupAsync(userManager, tenantId, state),
            _ => null,
        };

        if (user is null)
        {
            ErrorMessage = localizer["VerifyOtp.Invalid"].Value;
            return Page();
        }

        var claims = new List<Claim>();
        if (state.ExternalProvider is { } registrationId)
        {
            var providerName = registrationId.Contains(':', StringComparison.Ordinal)
                ? registrationId[(registrationId.LastIndexOf(':') + 1)..]
                : registrationId;
            claims.Add(new Claim(HuiaConstants.ClaimTypes.AuthenticationMethod, providerName));
            claims.Add(new Claim(HuiaConstants.ClaimTypes.ExternalIdp, registrationId));
            if (!string.IsNullOrEmpty(state.ExternalIdToken))
            {
                claims.Add(new Claim(HuiaConstants.ClaimTypes.ExternalIdToken, state.ExternalIdToken));
            }
        }
        else
        {
            claims.Add(new Claim(HuiaConstants.ClaimTypes.AuthenticationMethod, HuiaConstants.AuthenticationMethods.Sms));
        }

        await flow.SignInManager.SignInWithClaimsAsync(user, isPersistent: false, claims);
        await events.PublishAsync(new UserLoggedInEvent(
            tenantId, user.Id, claims[0].Value, null, timeProvider.GetUtcNow()));

        return await ResolvePostSignUpRedirectAsync(user, returnUrl);
    }

    private async Task<HuiaUser?> CompletePhoneSignupAsync(HuiaUserManager userManager, string tenantId, string pendingId, AuthFlowState state)
    {
        var pending = pendingSignups.Get(pendingId);
        var phoneNumber = pending?.PhoneNumber ?? state.PhoneNumber;
        if (phoneNumber is null)
        {
            return null;
        }

        var (result, user) = await userManager.CreatePhoneUserAsync(tenantId, phoneNumber, Input.FirstName, Input.LastName);
        if (!result.Succeeded)
        {
            ErrorMessage = string.Join(" ", result.Errors.Select(e => e.Description));
            return null;
        }

        pendingSignups.Remove(pendingId);
        await events.PublishAsync(new UserRegisteredEvent(
            tenantId, user.Id, user.UserName!, null, HuiaConstants.AuthenticationMethods.Sms, timeProvider.GetUtcNow()));
        return user;
    }

    private async Task<HuiaUser?> CompleteExistingUserAsync(HuiaUserManager userManager, string userId)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return null;
        }

        user.FirstName = Input.FirstName;
        user.LastName = Input.LastName;
        var result = await userManager.UpdateAsync(user);
        return result.Succeeded ? user : null;
    }

    private async Task<HuiaUser?> CompleteExternalSignupAsync(HuiaUserManager userManager, string tenantId, AuthFlowState state)
    {
        var email = state.Email;

        // Defensive: an account may have claimed this email since the callback decided to provision.
        if (!string.IsNullOrWhiteSpace(email) && await userManager.FindByEmailAsync(email) is not null)
        {
            ErrorMessage = localizer["Login.ExternalEmailTaken"].Value;
            return null;
        }

        var provider = state.ExternalProvider!;
        var key = state.ExternalProviderKey ?? string.Empty;
        var displayName = state.ExternalDisplayName ?? provider;

        var (create, user) = await userManager.CreateExternalUserAsync(
            tenantId, email, Input.FirstName, Input.LastName, provider, key, displayName);
        if (!create.Succeeded)
        {
            ErrorMessage = string.Join(" ", create.Errors.Select(e => e.Description));
            return null;
        }

        await events.PublishAsync(new UserRegisteredEvent(
            tenantId, user.Id, user.UserName!, user.Email, state.ExternalProvider!, timeProvider.GetUtcNow()));
        return user;
    }

    private void SetHeadings()
    {
        ViewData["Title"] = localizer["CompleteProfile.Title"].Value;
        ViewData["Heading"] = localizer["CompleteProfile.Heading"].Value;
    }

    /// <summary>The profile-completion form fields.</summary>
    public sealed class InputModel
    {
        /// <summary>Given name.</summary>
        [Required]
        [StringLength(256)]
        public string FirstName { get; set; } = string.Empty;

        /// <summary>Family name.</summary>
        [Required]
        [StringLength(256)]
        public string LastName { get; set; } = string.Empty;
    }
}
