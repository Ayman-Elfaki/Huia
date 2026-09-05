namespace Huia.Options;

/// <summary>
/// Per-tenant settings for passkey (WebAuthn / FIDO2) sign-in, enabled via
/// <see cref="HuiaTenantAuthenticationOptions.UsePasskeyLogin"/>. The relying-party identity itself
/// (id, name, allowed origins) is host-wide and lives on <see cref="HuiaPasskeyServerOptions"/> —
/// every tenant is served from the same origin under the base-path strategy, so there is a single
/// relying party and credentials are isolated per tenant by the database query filter, not by a
/// distinct relying-party id.
/// </summary>
public sealed class PasskeyOptions : IHuiaOptionsSection
{
    /// <summary>How strongly a ceremony must assert user verification. Defaults to <see cref="PasskeyUserVerification.Required"/>.</summary>
    public PasskeyUserVerification UserVerification { get; set; } = PasskeyUserVerification.Required;

    /// <summary>Which class of authenticator to prefer. Defaults to <see cref="PasskeyAuthenticatorAttachment.Any"/>.</summary>
    public PasskeyAuthenticatorAttachment AuthenticatorAttachment { get; set; } = PasskeyAuthenticatorAttachment.Any;

    /// <summary>
    /// Whether this tenant may use a registered passkey as a step-up second factor after a password
    /// sign-in. On by default; turning it off hides the "require a passkey as a second factor" toggle
    /// and rejects attempts to enable it.
    /// </summary>
    public bool AllowSecondFactor { get; set; } = true;

    /// <summary>How long the browser is given to complete a ceremony. Defaults to two minutes.</summary>
    public TimeSpan AuthenticatorTimeout { get; set; } = TimeSpan.FromMinutes(2);

    void IHuiaOptionsSection.Validate(string path, List<string> errors)
    {
        errors.Require(
            AuthenticatorTimeout >= TimeSpan.FromSeconds(30) && AuthenticatorTimeout <= TimeSpan.FromMinutes(10),
            HuiaOptionsValidation.Combine(path, nameof(AuthenticatorTimeout)),
            "must be between 30 seconds and 10 minutes.");
    }
}
