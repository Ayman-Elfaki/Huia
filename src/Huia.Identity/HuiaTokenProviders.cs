namespace Huia.Identity;

/// <summary>Well-known <see cref="IHuiaTokenProvider.Name"/> values for the three built-in providers
/// <c>AddHuia</c> registers. Replaces ASP.NET Core Identity's <c>TokenOptions.Default*Provider</c> constants.</summary>
public static class HuiaTokenProviders
{
    /// <summary><see cref="DataProtectorHuiaTokenProvider"/> — password reset, email confirmation, and
    /// change-phone-number tokens.</summary>
    public const string Default = "Default";

    /// <summary><see cref="PhoneOtpHuiaTokenProvider"/> — phone/SMS one-time codes.</summary>
    public const string Phone = "Phone";

    /// <summary><see cref="TotpHuiaTokenProvider"/> — TOTP authenticator-app codes.</summary>
    public const string Authenticator = "Authenticator";
}
