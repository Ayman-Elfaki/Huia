using Huia.Identity;

namespace Huia.Stores;

/// <summary>
/// Backs arbitrary named per-user token storage — password-reset/email-confirm/phone-OTP validation state,
/// the TOTP <c>AuthenticatorKey</c>, and hashed 2FA recovery codes all flow through this one small contract,
/// collapsing ASP.NET Core Identity's <c>IUserTokenStore</c>/<c>IUserAuthenticationTokenStore</c>/
/// <c>IUserTwoFactorRecoveryCodeStore</c>. A composite key of (user, <paramref name="loginProvider"/>,
/// <paramref name="name"/>) identifies a token.
/// </summary>
public interface IHuiaUserTokenStore
{
    /// <summary>
    /// Stores <paramref name="value"/> under (<paramref name="user"/>, <paramref name="loginProvider"/>,
    /// <paramref name="name"/>), replacing any existing value — or deletes the entry if
    /// <paramref name="value"/> is <see langword="null"/>.
    /// </summary>
    Task SetTokenAsync(HuiaUser user, string loginProvider, string name, string? value,
        CancellationToken cancellationToken);

    /// <summary>Retrieves the value stored under (<paramref name="user"/>, <paramref name="loginProvider"/>,
    /// <paramref name="name"/>), or <see langword="null"/> if none exists.</summary>
    Task<string?> GetTokenAsync(HuiaUser user, string loginProvider, string name, CancellationToken cancellationToken);
}
