using System.Text.Json;
using Finbuckle.MultiTenant.Abstractions;
using Huia.AspNetCore.Flows;
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
    HuiaPasskeyRegistrar registrar,
    IMultiTenantContextAccessor tenantAccessor,
    IHuiaEventPublisher events,
    IStringLocalizer<SharedResource> localizer,
    TimeProvider timeProvider) : HuiaAccountPageModel
{
    /// <summary>The user's registered passkeys.</summary>
    public IReadOnlyList<PasskeyView> Passkeys { get; private set; } = [];

    /// <summary>False when the account's only way to sign in is a single passkey — Remove is then blocked.</summary>
    public bool CanRemove { get; private set; } = true;

    /// <summary>Localized UI strings the client script needs, as a JSON object.</summary>
    public string StringsJson { get; private set; } = "{}";

    /// <summary>The tenant's client application home, when one is registered. Offered as a "back to app" link.</summary>
    public string? ClientHomeUrl { get; private set; }

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

        var outcome = await registrar.RegisterAsync(user, body.Credential.GetRawText(), body.Name);
        return outcome.Succeeded
            ? new JsonResult(new { id = outcome.CredentialId })
            : BadRequest(new { error = outcome.Error ?? "attestation_failed" });
    }

    /// <summary>Renames a credential (XHR from the inline name editor).</summary>
    public async Task<IActionResult> OnPostRenameAsync([FromBody] RenameBody body)
    {
        var user = await CurrentUserAsync();
        if (user is null)
        {
            return Unauthorized();
        }

        if (!TryDecode(body.Id, out var credentialId) || !await userManager.RenamePasskeyAsync(user, credentialId, body.Name ?? string.Empty))
        {
            return NotFound();
        }

        return new NoContentResult();
    }

    /// <summary>Removes a credential (a real form POST — confirmed inline by the client).</summary>
    public async Task<IActionResult> OnPostDeleteAsync(string id)
    {
        var user = await CurrentUserAsync();
        if (user is null)
        {
            return Redirect($"{PathBase}/identity/account/login");
        }

        if (TryDecode(id, out var credentialId) && await userManager.GetPasskeyAsync(user, credentialId) is not null)
        {
            if (!await userManager.CanRemovePasskeyAsync(user))
            {
                ErrorMessage = localizer["Passkey.OnlyMethod"].Value;
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

    private async Task<HuiaUser?> CurrentUserAsync() =>
        User.Identity?.IsAuthenticated == true ? await userManager.GetUserAsync(User) : null;

    private async Task LoadAsync(HuiaUser user)
    {
        Passkeys = (await userManager.GetPasskeysAsync(user))
            .Select(p => new PasskeyView(WebEncoders.Base64UrlEncode(p.CredentialId), p.Name, p.CreatedAt, p.IsBackedUp))
            .ToArray();
        CanRemove = await userManager.CanRemovePasskeyAsync(user);
        ClientHomeUrl = TenantClientHome.Resolve(Tenant);

        StringsJson = JsonSerializer.Serialize(new
        {
            device = new
            {
                platform = localizer["Passkey.Device.Platform"].Value,
                phone = localizer["Passkey.Device.Phone"].Value,
                securityKey = localizer["Passkey.Device.SecurityKey"].Value,
                fallback = localizer["Passkey.Device.Fallback"].Value,
            },
            renameLabel = localizer["Passkey.RenameLabel"].Value,
            unnamed = localizer["Passkey.Unnamed"].Value,
            removeConfirm = localizer["Passkey.RemoveConfirm"].Value,
            removeConfirmYes = localizer["Passkey.RemoveConfirmYes"].Value,
            cancel = localizer["Common.Cancel"].Value,
            registerFailed = localizer["Passkey.Failed"].Value,
        });
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

    /// <summary>Body of the rename handler.</summary>
    /// <param name="Id">The base64url credential id.</param>
    /// <param name="Name">The new friendly name.</param>
    public sealed record RenameBody(string Id, string? Name);
}
