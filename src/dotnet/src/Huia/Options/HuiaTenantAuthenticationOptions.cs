namespace Huia.Options;

/// <summary>
/// The sign-in methods available for a tenant: email/password, phone (SMS one-time code), and external
/// identity providers. Each is independently opt-in through <see cref="UseEmailAndPasswordLogin"/> /
/// <see cref="UsePhoneLogin"/> / <see cref="UseExternalLogin"/> — the underlying option objects are not
/// part of the public surface.
/// </summary>
public sealed class HuiaTenantAuthenticationOptions : IHuiaOptionsSection
{
    /// <summary>The interactive email/password flow options. Configured via <see cref="UseEmailAndPasswordLogin"/>.</summary>
    internal EmailAndPasswordLoginOptions EmailAndPassword { get; } = new();

    /// <summary>The phone (SMS one-time code) flow options, or <see langword="null"/> when never enabled. Configured via <see cref="UsePhoneLogin"/>.</summary>
    internal PhoneOptions? Phone { get; private set; }

    /// <summary>The external-provider flow options, or <see langword="null"/> when never enabled. Configured via <see cref="UseExternalLogin"/>.</summary>
    internal ExternalLoginOptions? External { get; private set; }

    /// <summary>The passkey (WebAuthn) flow options, or <see langword="null"/> when never enabled. Configured via <see cref="UsePasskeyLogin"/>.</summary>
    internal PasskeyOptions? Passkey { get; private set; }

    /// <summary>Whether the interactive email/password flow is enabled for this tenant.</summary>
    public bool IsEmailAndPasswordLoginEnabled => EmailAndPassword.Enabled;

    /// <summary>Whether the phone (SMS one-time code) sign-in flow is enabled.</summary>
    public bool IsPhoneLoginEnabled => Phone is not null;

    /// <summary>Whether at least one external identity provider is configured.</summary>
    public bool IsExternalLoginEnabled => External is { Providers.Count: > 0 };

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

    /// <summary>Enables external identity providers for this tenant.</summary>
    /// <param name="configure">Configuration that registers at least one provider.</param>
    /// <returns>This instance, for chaining.</returns>
    public HuiaTenantAuthenticationOptions UseExternalLogin(Action<ExternalLoginOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        External ??= new ExternalLoginOptions();
        configure(External);
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

    /// <summary>Runs validation across all three sign-in methods.</summary>
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

        if (External is not null)
        {
            ((IHuiaOptionsSection)External).Validate(HuiaOptionsValidation.Combine(path, nameof(External)), errors);
        }

        if (Passkey is not null)
        {
            ((IHuiaOptionsSection)Passkey).Validate(HuiaOptionsValidation.Combine(path, nameof(Passkey)), errors);
        }

        var anyMethod = EmailAndPassword.Enabled || Phone is not null || IsExternalLoginEnabled || Passkey is not null;
        errors.Require(anyMethod, path, "at least one sign-in method must be enabled (email and password, phone, external, or passkey).");
    }
}
