namespace Huia.Identity;

/// <summary>
/// How an account signs in. Determines which contact details it may change through <c>/manage/*</c>:
/// a password or external account owns an email and must not carry a phone number; a phone-login
/// account owns its number (which doubles as the username) and must not carry an email. Resolved by
/// <see cref="HuiaUserManager.GetUserTypeAsync"/>.
/// </summary>
public enum HuiaUserType
{
    /// <summary>No password, no external login and no phone number — an incomplete record.</summary>
    Unknown = 0,

    /// <summary>Has a local password. Signs in with email + password.</summary>
    Password = 1,

    /// <summary>Has an external (OIDC) login and no password. Signs in through the provider.</summary>
    External = 2,

    /// <summary>No password and no external login, but a phone number. Signs in with an SMS one-time code.</summary>
    Phone = 3,
}
