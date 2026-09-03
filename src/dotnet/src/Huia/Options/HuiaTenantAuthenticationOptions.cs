namespace Huia.Options;

/// <summary>
/// The sign-in methods available for a tenant: interactive password and passwordless. Configure them
/// through <see cref="UsePasswordFlow"/> / <see cref="UsePasswordlessFlow"/> — the underlying option
/// objects are not part of the public surface.
/// </summary>
public sealed class HuiaTenantAuthenticationOptions : IHuiaOptionsSection
{
    /// <summary>The interactive username/password flow options. Configured via <see cref="UsePasswordFlow"/>.</summary>
    internal PasswordFlowOptions Password { get; } = new();

    /// <summary>The passwordless umbrella (phone one-time code and external providers). Configured via <see cref="UsePasswordlessFlow"/>.</summary>
    internal PasswordlessFlowOptions Passwordless { get; } = new();

    /// <summary>Whether the interactive password flow is enabled for this tenant.</summary>
    public bool IsPasswordEnabled => Password.Enabled;

    /// <summary>Whether the passwordless phone (SMS one-time code) sign-in is enabled.</summary>
    public bool IsPhoneLoginEnabled => Passwordless.IsPhoneLoginEnabled;

    /// <summary>Whether at least one external identity provider is configured.</summary>
    public bool IsExternalLoginEnabled => Passwordless.IsExternalLoginEnabled;

    /// <summary>
    /// Enables and configures the interactive password flow. Sugar over <see cref="Password"/> that
    /// mirrors <see cref="UsePasswordlessFlow"/>: calling it sets <see cref="PasswordFlowOptions.Enabled"/>.
    /// </summary>
    /// <param name="configure">Optional further configuration for the password flow.</param>
    /// <returns>This instance, for chaining.</returns>
    public HuiaTenantAuthenticationOptions UsePasswordFlow(Action<PasswordFlowOptions>? configure = null)
    {
        Password.Enabled = true;
        configure?.Invoke(Password);
        return this;
    }

    /// <summary>Enables and configures the passwordless umbrella. Sugar over <see cref="Passwordless"/>.</summary>
    /// <param name="configure">Configuration for the passwordless sub-flows.</param>
    /// <returns>This instance, for chaining.</returns>
    public HuiaTenantAuthenticationOptions UsePasswordlessFlow(Action<PasswordlessFlowOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(Passwordless);
        return this;
    }

    /// <summary>Runs validation across both sign-in method groups.</summary>
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
        ((IHuiaOptionsSection)Password).Validate(HuiaOptionsValidation.Combine(path, nameof(Password)), errors);
        ((IHuiaOptionsSection)Passwordless).Validate(HuiaOptionsValidation.Combine(path, nameof(Passwordless)), errors);

        var anyMethod = Password.Enabled || Passwordless.IsPhoneLoginEnabled || Passwordless.IsExternalLoginEnabled;
        errors.Require(anyMethod, path, "at least one sign-in method must be enabled (password, phone, or external).");
    }
}
