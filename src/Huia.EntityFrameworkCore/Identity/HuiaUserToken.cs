namespace Huia.EntityFrameworkCore.Identity;

/// <summary>Entity backing <see cref="Huia.Stores.IHuiaUserTokenStore"/> — password-reset/email-confirm/
/// phone-OTP validation state, the TOTP <c>AuthenticatorKey</c>, and hashed 2FA recovery codes. Composite key
/// (<see cref="UserId"/>, <see cref="Provider"/>, <see cref="Name"/>).</summary>
public sealed class HuiaUserToken
{
    /// <summary>The user this token belongs to.</summary>
    public required string UserId { get; set; }

    /// <summary>The provider/purpose namespace (e.g. <c>"Authenticator"</c>).</summary>
    public required string Provider { get; set; }

    /// <summary>The token's name within <see cref="Provider"/> (e.g. <c>"AuthenticatorKey"</c>).</summary>
    public required string Name { get; set; }

    /// <summary>The stored value.</summary>
    public string? Value { get; set; }
}
