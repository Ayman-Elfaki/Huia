using System.Text.Json;
using Finbuckle.MultiTenant.Abstractions;
using Huia.AspNetCore.Identity;
using Huia.AspNetCore.UI;
using Huia.EntityFrameworkCore.Entities;
using Huia.EntityFrameworkCore.Multitenancy;
using Huia.Events;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Localization;

namespace Huia.AspNetCore.Areas.Identity.Pages.Account;

/// <summary>
/// Cookie-authenticated passkey management for browser users who signed in directly at the identity
/// provider. The Nuxt/relying-party clients use the equivalent token-protected <c>manage/passkeys/*</c>
/// API instead.
/// </summary>
public sealed class PasskeysModel(
    HuiaUserManager userManager,
    HuiaSignInManager signInManager,
    IMultiTenantContextAccessor tenantAccessor,
    IHuiaEventPublisher events,
    IStringLocalizer<SharedResource> localizer,
    TimeProvider timeProvider) : HuiaAccountPageModel
{
    /// <summary>The user's registered passkeys.</summary>
    public IReadOnlyList<PasskeyView> Passkeys { get; private set; } = [];

    /// <summary>Whether a passkey is currently required as a second factor.</summary>
    public bool TwoFactorEnabled { get; private set; }

    /// <summary>How many two-factor recovery codes remain unused.</summary>
    public int RecoveryCodesLeft { get; private set; }

    /// <summary>Recovery codes to display once, immediately after enabling the second factor.</summary>
    public IReadOnlyList<string> NewRecoveryCodes { get; private set; } = [];

    /// <summary>Handles the GET.</summary>
    public async Task<IActionResult> OnGetAsync()
    {
        var user = await CurrentUserAsync();
        if (user is null)
        {
            return Redirect($"{PathBase}/identity/account/login");
        }

        if (!IsPasskeyLoginEnabled)
        {
            return NotFound();
        }

        SetHeadings();
        await LoadAsync(user);
        return Page();
    }

    /// <summary>Returns the WebAuthn creation options for the current user (JSON).</summary>
    public async Task<IActionResult> OnPostCreationOptionsAsync()
    {
        var user = await CurrentUserAsync();
        if (user is null || !IsPasskeyLoginEnabled)
        {
            return NotFound();
        }

        var displayName = $"{user.FirstName} {user.LastName}".Trim();
        var entity = new PasskeyUserEntity
        {
            Id = user.Id,
            Name = user.UserName ?? user.Email ?? user.Id,
            DisplayName = displayName.Length > 0 ? displayName : (user.UserName ?? user.Id),
        };

        return Content(await signInManager.MakePasskeyCreationOptionsAsync(entity), "application/json");
    }

    /// <summary>Registers the attested credential.</summary>
    public async Task<IActionResult> OnPostRegisterAsync([FromBody] RegisterBody body)
    {
        var user = await CurrentUserAsync();
        if (user is null || !IsPasskeyLoginEnabled)
        {
            return NotFound();
        }

        PasskeyAttestationResult attestation;
        try
        {
            attestation = await signInManager.PerformPasskeyAttestationAsync(body.Credential.GetRawText());
        }
        catch (InvalidOperationException)
        {
            return BadRequest(new { error = "no_registration_in_progress" });
        }

        if (!attestation.Succeeded || attestation.Passkey is not { } passkeyInfo)
        {
            return BadRequest(new { error = attestation.Failure?.Message ?? "attestation_failed" });
        }

        passkeyInfo.Name = string.IsNullOrWhiteSpace(body.Name) ? null : body.Name.Trim();
        var result = await userManager.AddOrUpdatePasskeyAsync(user, passkeyInfo);
        if (!result.Succeeded)
        {
            return BadRequest(new { error = string.Join("; ", result.Errors.Select(e => e.Description)) });
        }

        var id = WebEncoders.Base64UrlEncode(passkeyInfo.CredentialId);
        await events.PublishAsync(new PasskeyRegisteredEvent(
            tenantAccessor.RequireCurrentTenantId(), user.Id, id.Length <= 12 ? id : id[..12], timeProvider.GetUtcNow()));
        return new JsonResult(new { id });
    }

    /// <summary>Renames a credential.</summary>
    public async Task<IActionResult> OnPostRenameAsync(string id, string name)
    {
        var user = await CurrentUserAsync();
        if (user is null)
        {
            return Redirect($"{PathBase}/identity/account/login");
        }

        if (TryDecode(id, out var credentialId))
        {
            await userManager.RenamePasskeyAsync(user, credentialId, name ?? string.Empty);
        }

        return RedirectToPage();
    }

    /// <summary>Removes a credential.</summary>
    public async Task<IActionResult> OnPostDeleteAsync(string id)
    {
        var user = await CurrentUserAsync();
        if (user is null)
        {
            return Redirect($"{PathBase}/identity/account/login");
        }

        if (TryDecode(id, out var credentialId) && await userManager.GetPasskeyAsync(user, credentialId) is not null)
        {
            if (user.TwoFactorEnabled && await userManager.CountPasskeysAsync(user) <= 1)
            {
                ErrorMessage = localizer["Passkey.CannotRemoveLast"].Value;
            }
            else
            {
                await userManager.RemovePasskeyAsync(user, credentialId);
                await events.PublishAsync(new PasskeyRemovedEvent(
                    tenantAccessor.RequireCurrentTenantId(), user.Id, id.Length <= 12 ? id : id[..12], timeProvider.GetUtcNow()));
            }
        }

        if (ErrorMessage is not null)
        {
            SetHeadings();
            await LoadAsync(user);
            return Page();
        }

        return RedirectToPage();
    }

    /// <summary>Enables or disables the passkey second factor.</summary>
    public async Task<IActionResult> OnPostTwoFactorAsync(bool enabled)
    {
        var user = await CurrentUserAsync();
        if (user is null)
        {
            return Redirect($"{PathBase}/identity/account/login");
        }

        SetHeadings();

        if (enabled)
        {
            if (!IsPasskeySecondFactorAllowed || !await userManager.HasPasskeyAsync(user))
            {
                ErrorMessage = localizer["Passkey.NeedOneBeforeTwoFactor"].Value;
                await LoadAsync(user);
                return Page();
            }

            await userManager.SetTwoFactorEnabledAsync(user, true);
            NewRecoveryCodes = (await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10))?.ToArray() ?? [];
        }
        else
        {
            await userManager.SetTwoFactorEnabledAsync(user, false);
        }

        await LoadAsync(user);
        return Page();
    }

    private async Task<HuiaUser?> CurrentUserAsync() =>
        User.Identity?.IsAuthenticated == true ? await userManager.GetUserAsync(User) : null;

    private async Task LoadAsync(HuiaUser user)
    {
        Passkeys = (await userManager.GetPasskeysAsync(user))
            .Select(p => new PasskeyView(WebEncoders.Base64UrlEncode(p.CredentialId), p.Name, p.CreatedAt, p.IsBackedUp))
            .ToArray();
        TwoFactorEnabled = user.TwoFactorEnabled;
        RecoveryCodesLeft = await userManager.CountRecoveryCodesAsync(user);
    }

    private void SetHeadings()
    {
        ViewData["Title"] = localizer["Passkey.ManageTitle"].Value;
        ViewData["Heading"] = localizer["Passkey.ManageHeading"].Value;
    }

    private static bool TryDecode(string id, out byte[] credentialId)
    {
        try
        {
            credentialId = WebEncoders.Base64UrlDecode(id);
            return credentialId.Length > 0;
        }
        catch (FormatException)
        {
            credentialId = [];
            return false;
        }
    }

    /// <summary>A row in the passkey list.</summary>
    /// <param name="Id">The base64url credential id.</param>
    /// <param name="Name">The friendly name, if set.</param>
    /// <param name="CreatedAt">When the credential was registered.</param>
    /// <param name="IsBackedUp">Whether the authenticator reports the credential is synced / backed up.</param>
    public sealed record PasskeyView(string Id, string? Name, DateTimeOffset CreatedAt, bool IsBackedUp);

    /// <summary>Body of the register handler.</summary>
    /// <param name="Credential">The WebAuthn attestation from <c>navigator.credentials.create</c>.</param>
    /// <param name="Name">A friendly name for the credential.</param>
    public sealed record RegisterBody(JsonElement Credential, string? Name);
}
