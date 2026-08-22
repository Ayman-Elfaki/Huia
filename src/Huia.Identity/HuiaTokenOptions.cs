namespace Huia.Identity;

/// <summary>Per-purpose token lifetimes/provider names. Mirrors ASP.NET Core Identity's <c>TokenOptions</c>.</summary>
public sealed class HuiaTokenOptions
{
    /// <summary>The <see cref="IHuiaTokenProvider.Name"/> used for password-reset/email-confirmation/change-
    /// phone-number tokens. Defaults to <see cref="HuiaTokenProviders.Default"/>.</summary>
    public string DefaultProvider { get; set; } = HuiaTokenProviders.Default;

    /// <summary>The <see cref="IHuiaTokenProvider.Name"/> used for phone/SMS OTP tokens. Defaults to
    /// <see cref="HuiaTokenProviders.Phone"/>.</summary>
    public string PhoneProvider { get; set; } = HuiaTokenProviders.Phone;

    /// <summary>The <see cref="IHuiaTokenProvider.Name"/> used for TOTP authenticator-app tokens. Defaults to
    /// <see cref="HuiaTokenProviders.Authenticator"/>.</summary>
    public string AuthenticatorTokenProvider { get; set; } = HuiaTokenProviders.Authenticator;

    /// <summary>How long a password-reset/email-confirmation/change-phone-number token
    /// (<see cref="HuiaTokenProviders.Default"/>) stays valid. Defaults to 1 day.</summary>
    public TimeSpan DefaultTokenLifetime { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    /// How long a phone/SMS OTP (<see cref="HuiaTokenProviders.Phone"/>) stays valid — independently
    /// configurable from <see cref="DefaultTokenLifetime"/>, unlike ASP.NET Core Identity's built-in phone
    /// token provider (see docs/passwordless.md). Defaults to 5 minutes.
    /// </summary>
    public TimeSpan PhoneOtpLifetime { get; set; } = TimeSpan.FromMinutes(5);
}
