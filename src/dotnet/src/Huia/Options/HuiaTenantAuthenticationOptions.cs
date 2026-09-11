namespace Huia.Options;

/// <summary>
/// The sign-in methods available for a tenant: email/password and phone (SMS one-time code).
/// Each is independently opt-in through <see cref="UseEmailAndPasswordLogin"/> /
/// <see cref="UsePhoneLogin"/> — the underlying option objects are not part of the public surface.
/// </summary>
public sealed class HuiaTenantAuthenticationOptions : IHuiaOptionsSection
{
    /// <summary>The interactive email/password flow options. Configured via <see cref="UseEmailAndPasswordLogin"/>.</summary>
    internal EmailAndPasswordLoginOptions EmailAndPassword { get; } = new();

    /// <summary>The phone (SMS one-time code) flow options, or <see langword="null"/> when never enabled. Configured via <see cref="UsePhoneLogin"/>.</summary>
    internal PhoneOptions? Phone { get; private set; }

    /// <summary>The passkey (WebAuthn) flow options, or <see langword="null"/> when never enabled. Configured via <see cref="UsePasskeyLogin"/>.</summary>
    internal PasskeyOptions? Passkey { get; private set; }

    /// <summary>Hook for extensions (e.g. OpenId external login) to indicate a sign-in method is enabled.</summary>
    internal Func<bool>? HasAdditionalLoginMethod { get; set; }

    /// <summary>Whether the interactive email/password flow is enabled for this tenant.</summary>
    public bool IsEmailAndPasswordLoginEnabled => EmailAndPassword.Enabled;

    /// <summary>Whether the phone (SMS one-time code) sign-in flow is enabled.</summary>
    public bool IsPhoneLoginEnabled => Phone is not null;

    /// <summary>Whether passkey (WebAuthn) sign-in is enabled for this tenant.</summary>
    public bool IsPasskeyLoginEnabled => Passkey is not null;

    /// <summary>
    /// Enables and configures the interactive email/password flow. Calling it sets
    /// <see cref="EmailAndPasswordLoginOptions.Enabled"/>.
    /// </summary>
    /// <param name="configure">Optional further configuration for the flow.</param>
    /// <returns>This instance, for chaining.</returns>
    public HuiaTenantAuthenticationOptions UseEmailAndPasswordLogin(Action<EmailAndPasswordLoginOptions>? configure = null)
    {
        EmailAndPassword.Enabled = true;
        configure?.Invoke(EmailAndPassword);
        return this;
    }

    /// <summary>Enables the passwordless phone (SMS one-time code) sign-in for this tenant.</summary>
    /// <param name="configure">Optional configuration for the flow.</param>
    /// <returns>This instance, for chaining.</returns>
    public HuiaTenantAuthenticationOptions UsePhoneLogin(Action<PhoneOptions>? configure = null)
    {
        Phone ??= new PhoneOptions();
        configure?.Invoke(Phone);
        return this;
    }

    /// <summary>
    /// Enables passkey (WebAuthn / FIDO2) sign-in for this tenant: a discoverable one-tap sign-in on the
    /// login page and a one-time prompt to set one up after a first sign-in.
    /// </summary>
    /// <param name="configure">Optional configuration for the flow.</param>
    /// <returns>This instance, for chaining.</returns>
    public HuiaTenantAuthenticationOptions UsePasskeyLogin(Action<PasskeyOptions>? configure = null)
    {
        Passkey ??= new PasskeyOptions();
        configure?.Invoke(Passkey);
        return this;
    }

    /// <summary>
    /// Turns off every anonymous account-creation path: self-service email/password registration, and
    /// — when the phone flow is enabled — phone auto-provisioning (an unknown, well-formed number
    /// starting its own sign-up). Only an administrator can create accounts afterwards.
    /// </summary>
    /// <returns>This instance, for chaining.</returns>
    public HuiaTenantAuthenticationOptions DisableRegistration()
    {
        EmailAndPassword.AllowSelfServiceRegistration = false;
        if (Phone is not null)
        {
            Phone.AllowAutoProvisioning = false;
        }

        return this;
    }

    /// <summary>Runs validation across all configured sign-in methods.</summary>
    public void Validate()
    {
        var errors = new List<string>();
        ((IHuiaOptionsSection)this).Validate(HuiaConstants.ConfigurationSection + ":Authentication", errors);
        if (errors.Count > 0)
        {
            throw new HuiaOptionsException(errors);
        }
    }

    void IHuiaOptionsSection.Validate(string path, List<string> errors)
    {
        ((IHuiaOptionsSection)EmailAndPassword).Validate(HuiaOptionsValidation.Combine(path, nameof(EmailAndPassword)), errors);
        if (Phone is not null)
        {
            ((IHuiaOptionsSection)Phone).Validate(HuiaOptionsValidation.Combine(path, nameof(Phone)), errors);
        }

        if (Passkey is not null)
        {
            ((IHuiaOptionsSection)Passkey).Validate(HuiaOptionsValidation.Combine(path, nameof(Passkey)), errors);
        }

        var anyMethod = EmailAndPassword.Enabled || Phone is not null || Passkey is not null || HasAdditionalLoginMethod?.Invoke() == true;
        errors.Require(anyMethod, path, "at least one sign-in method must be enabled (email and password, phone, external, or passkey).");
    }
}
