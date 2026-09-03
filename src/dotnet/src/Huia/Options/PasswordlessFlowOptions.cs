namespace Huia.Options;

/// <summary>
/// Umbrella for the non-password sign-in methods. Each sub-flow is opt-in and independently nullable:
/// phone (SMS one-time code) via <see cref="UsePhoneLogin"/> and external providers via
/// <see cref="UseExternalLogin"/>.
/// </summary>
public sealed class PasswordlessFlowOptions : IHuiaOptionsSection
{
    /// <summary>The phone (SMS OTP) sub-flow options, or <see langword="null"/> when it was never enabled.</summary>
    public PhoneLoginOptions? PhoneLogin { get; private set; }

    /// <summary>The external-provider sub-flow options, or <see langword="null"/> when it was never enabled.</summary>
    public ExternalLoginOptions? ExternalLogin { get; private set; }

    /// <summary>Whether the phone sign-in sub-flow is enabled.</summary>
    public bool IsPhoneLoginEnabled => PhoneLogin is not null;

    /// <summary>Whether at least one external provider is registered.</summary>
    public bool IsExternalLoginEnabled => ExternalLogin is { Providers.Count: > 0 };

    /// <summary>
    /// Default region (ISO 3166-1 alpha-2, for example <c>US</c>) used to interpret phone numbers entered
    /// without a country code. Format-checked here only; an unknown-but-well-formed code degrades
    /// gracefully at runtime.
    /// </summary>
    public string? DefaultCountry { get; set; }

    /// <summary>Enables the passwordless phone (SMS one-time code) sign-in for this tenant.</summary>
    /// <param name="configure">Optional configuration for the sub-flow.</param>
    /// <returns>This instance, for chaining.</returns>
    public PasswordlessFlowOptions UsePhoneLogin(Action<PhoneLoginOptions>? configure = null)
    {
        PhoneLogin ??= new PhoneLoginOptions();
        configure?.Invoke(PhoneLogin);
        return this;
    }

    /// <summary>Enables external identity providers for this tenant.</summary>
    /// <param name="configure">Configuration that registers at least one provider.</param>
    /// <returns>This instance, for chaining.</returns>
    public PasswordlessFlowOptions UseExternalLogin(Action<ExternalLoginOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        ExternalLogin ??= new ExternalLoginOptions();
        configure(ExternalLogin);
        return this;
    }

    void IHuiaOptionsSection.Validate(string path, List<string> errors)
    {
        if (!string.IsNullOrWhiteSpace(DefaultCountry))
        {
            errors.Require(DefaultCountry.Length == 2 && DefaultCountry.All(static c => c is >= 'A' and <= 'Z'),
                HuiaOptionsValidation.Combine(path, nameof(DefaultCountry)),
                "must be a two-letter upper-case ISO 3166-1 alpha-2 code.");
        }

        if (PhoneLogin is not null)
        {
            ((IHuiaOptionsSection)PhoneLogin).Validate(HuiaOptionsValidation.Combine(path, nameof(PhoneLogin)), errors);
        }

        if (ExternalLogin is not null)
        {
            ((IHuiaOptionsSection)ExternalLogin).Validate(HuiaOptionsValidation.Combine(path, nameof(ExternalLogin)), errors);
        }
    }
}
