using Huia.AspNetCore.Flows;
using Huia.AspNetCore.Identity;
using Huia.AspNetCore.UI;
using Huia.EntityFrameworkCore.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace Huia.AspNetCore.Areas.Identity.Pages.Account;

/// <summary>
/// Shown once, right after an account is created and signed in, when the tenant offers passkeys: a
/// skippable prompt to create a discoverable passkey. Reached from
/// <see cref="HuiaAccountPageModel.ResolvePostSignUpRedirectAsync"/>; both "Skip" and a successful
/// setup mark the account so it is never prompted again, then continue to the original return URL.
/// </summary>
public sealed class PasskeyEnrollModel(
    HuiaUserManager userManager,
    HuiaSignInManager signInManager,
    HuiaPasskeyRegistrar registrar,
    IReturnUrlProtector returnUrlProtector,
    IStringLocalizer<SharedResource> localizer) : HuiaAccountPageModel
{
    /// <summary>Where to send the browser once the prompt is dealt with.</summary>
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

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

        // Direct navigation after the fact: if the account already has a passkey or was already
        // prompted, there is nothing to do here.
        if (await userManager.CountPasskeysAsync(user) > 0 || await IsPromptedAsync(user))
        {
            return Redirect(Sanitized());
        }

        ReturnUrl = Sanitized();
        ViewData["Title"] = localizer["Passkey.EnrollTitle"].Value;
        ViewData["Heading"] = localizer["Passkey.EnrollHeading"].Value;
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

    /// <summary>Registers the attested credential and marks the account prompted.</summary>
    public async Task<IActionResult> OnPostRegisterAsync([FromBody] RegisterBody body)
    {
        var user = await CurrentUserAsync();
        if (user is null || !IsPasskeyLoginEnabled)
        {
            return NotFound();
        }

        var outcome = await registrar.RegisterAsync(user, body.Credential.GetRawText(), body.Name);
        if (!outcome.Succeeded)
        {
            return BadRequest(new { error = outcome.Error ?? "attestation_failed" });
        }

        await MarkPromptedAsync(user);
        return new JsonResult(new { redirectUrl = Sanitized() });
    }

    /// <summary>Skips the prompt (marks the account so it is not shown again) and continues.</summary>
    public async Task<IActionResult> OnPostSkipAsync()
    {
        var user = await CurrentUserAsync();
        if (user is not null)
        {
            await MarkPromptedAsync(user);
        }

        return Redirect(Sanitized());
    }

    private string Sanitized() => returnUrlProtector.SanitizeReturnUrl(ReturnUrl, HttpContext);

    private async Task<HuiaUser?> CurrentUserAsync() =>
        User.Identity?.IsAuthenticated == true ? await userManager.GetUserAsync(User) : null;

    private async Task<bool> IsPromptedAsync(HuiaUser user) =>
        await userManager.GetAuthenticationTokenAsync(user, HuiaConstants.PasskeyLoginProvider, HuiaConstants.EnrollPromptedTokenName) is not null;

    private Task MarkPromptedAsync(HuiaUser user) =>
        userManager.SetAuthenticationTokenAsync(user, HuiaConstants.PasskeyLoginProvider, HuiaConstants.EnrollPromptedTokenName, "1");

    /// <summary>Body of the register handler.</summary>
    /// <param name="Credential">The WebAuthn attestation from <c>navigator.credentials.create</c>.</param>
    /// <param name="Name">A friendly name for the credential.</param>
    public sealed record RegisterBody(System.Text.Json.JsonElement Credential, string? Name);
}
