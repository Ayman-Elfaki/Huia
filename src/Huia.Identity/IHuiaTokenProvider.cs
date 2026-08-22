namespace Huia.Identity;

/// <summary>
/// Generates and validates purpose-bound, per-user tokens — the pluggable extension point behind
/// <see cref="HuiaUserManager.GenerateTwoFactorTokenAsync"/>/<see cref="HuiaUserManager.VerifyTwoFactorTokenAsync"/>
/// and the password-reset/email-confirmation/change-phone-number token pair. Replaces ASP.NET Core Identity's
/// <c>IUserTwoFactorTokenProvider&lt;TUser&gt;</c>. Register a custom implementation as an additional
/// <c>IHuiaTokenProvider</c> in DI to add a new auth method without changing <see cref="HuiaUserManager"/>.
/// </summary>
public interface IHuiaTokenProvider
{
    /// <summary>This provider's unique name — how callers select it (e.g. <see cref="HuiaTokenProviders.Phone"/>).</summary>
    string Name { get; }

    /// <summary>Generates a token for <paramref name="user"/>/<paramref name="purpose"/>.</summary>
    Task<string> GenerateAsync(HuiaUser user, string purpose, CancellationToken cancellationToken);

    /// <summary>Validates <paramref name="token"/> for <paramref name="user"/>/<paramref name="purpose"/>,
    /// in constant time where the comparison involves secret material.</summary>
    Task<bool> ValidateAsync(HuiaUser user, string purpose, string token, CancellationToken cancellationToken);
}
