using Huia.EntityFrameworkCore.Entities;
using Microsoft.AspNetCore.Identity;

namespace Huia.AspNetCore.Identity;

/// <summary>
/// How an account signs in. Determines which contact details it may change through <c>/manage/*</c>:
/// a password or external account owns an email and must not carry a phone number; a phone-login
/// account owns its number (which doubles as the username) and must not carry an email.
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

/// <summary>Classifies a <see cref="HuiaUser"/> by how it authenticates.</summary>
public static class HuiaUserTypeExtensions
{
    /// <summary>
    /// Resolves the account's <see cref="HuiaUserType"/>. Precedence is password &#8594; external
    /// &#8594; phone: a record with a password is a <see cref="HuiaUserType.Password"/> account even
    /// if it also has an external login.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="user">The account to classify.</param>
    /// <returns>The resolved type.</returns>
    public static async Task<HuiaUserType> ResolveTypeAsync(this UserManager<HuiaUser> userManager, HuiaUser user)
    {
        ArgumentNullException.ThrowIfNull(userManager);
        ArgumentNullException.ThrowIfNull(user);

        if (await userManager.HasPasswordAsync(user))
        {
            return HuiaUserType.Password;
        }

        if ((await userManager.GetLoginsAsync(user)).Count > 0)
        {
            return HuiaUserType.External;
        }

        return string.IsNullOrEmpty(user.PhoneNumber) ? HuiaUserType.Unknown : HuiaUserType.Phone;
    }
}
