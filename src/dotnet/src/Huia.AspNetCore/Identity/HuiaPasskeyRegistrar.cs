using Finbuckle.MultiTenant.Abstractions;
using Huia.EntityFrameworkCore.Entities;
using Huia.EntityFrameworkCore.Multitenancy;
using Huia.Events;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace Huia.AspNetCore.Identity;

/// <summary>Why a passkey registration did not complete.</summary>
public enum PasskeyRegistrationStatus
{
    /// <summary>The credential was verified and stored.</summary>
    Registered,

    /// <summary>No creation ceremony was in progress (options were never requested, or the state cookie expired).</summary>
    NoCeremony,

    /// <summary>The attestation the browser sent did not verify.</summary>
    AttestationFailed,

    /// <summary>The credential verified but the store rejected it.</summary>
    StoreFailed,
}

/// <summary>The outcome of <see cref="HuiaPasskeyRegistrar.RegisterAsync"/>.</summary>
/// <param name="Status">What happened.</param>
/// <param name="CredentialId">The base64url credential id when <see cref="Status"/> is <see cref="PasskeyRegistrationStatus.Registered"/>.</param>
/// <param name="Error">A human-readable reason when it is not.</param>
public readonly record struct PasskeyRegistrationOutcome(PasskeyRegistrationStatus Status, string? CredentialId, string? Error)
{
    /// <summary>Whether the credential was stored.</summary>
    public bool Succeeded => Status == PasskeyRegistrationStatus.Registered;
}

/// <summary>
/// The shared "finish a passkey registration" step: verify the attestation the browser sent, store the
/// credential, raise <see cref="PasskeyRegisteredEvent"/>. Used by the bearer <c>manage/passkeys</c>
/// endpoint, the cookie-authenticated <c>Passkeys</c> management page and the post-sign-up
/// <c>PasskeyEnroll</c> interstitial so all three share one code path.
/// </summary>
public sealed class HuiaPasskeyRegistrar(
    HuiaSignInManager signInManager,
    HuiaUserManager userManager,
    IMultiTenantContextAccessor tenantAccessor,
    IHuiaEventPublisher events,
    TimeProvider timeProvider)
{
    /// <summary>Verifies and stores the attested credential for <paramref name="user"/>.</summary>
    /// <param name="user">The account the credential belongs to.</param>
    /// <param name="credentialJson">The serialized WebAuthn attestation from <c>navigator.credentials.create</c>.</param>
    /// <param name="name">A friendly name for the credential, or <see langword="null"/>.</param>
    public async Task<PasskeyRegistrationOutcome> RegisterAsync(HuiaUser user, string credentialJson, string? name)
    {
        ArgumentNullException.ThrowIfNull(user);

        PasskeyAttestationResult attestation;
        try
        {
            attestation = await signInManager.PerformPasskeyAttestationAsync(credentialJson);
        }
        catch (InvalidOperationException)
        {
            return new(PasskeyRegistrationStatus.NoCeremony, null, "No passkey registration is in progress.");
        }

        if (!attestation.Succeeded || attestation.Passkey is not { } passkeyInfo)
        {
            return new(PasskeyRegistrationStatus.AttestationFailed, null,
                attestation.Failure?.Message ?? "The passkey could not be registered.");
        }

        passkeyInfo.Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        var result = await userManager.AddOrUpdatePasskeyAsync(user, passkeyInfo);
        if (!result.Succeeded)
        {
            return new(PasskeyRegistrationStatus.StoreFailed, null,
                string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        var id = WebEncoders.Base64UrlEncode(passkeyInfo.CredentialId);
        await events.PublishAsync(new PasskeyRegisteredEvent(
            tenantAccessor.RequireCurrentTenantId(), user.Id, id.Length <= 12 ? id : id[..12], timeProvider.GetUtcNow()));

        return new(PasskeyRegistrationStatus.Registered, id, null);
    }
}
