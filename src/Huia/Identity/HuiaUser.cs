using Microsoft.AspNetCore.Identity;

namespace Huia.Identity;

/// <summary>
/// Base ASP.NET Core Identity user type used by Huia. Abstract: every account is either a
/// <see cref="Huia.Identity.PhoneUser"/> (phone-only, passwordless) or a
/// <see cref="Huia.Identity.StandardUser"/> (email/password and/or external-login), mapped via EF Core's
/// table-per-concrete-type (TPC) inheritance strategy to their own tables. Inherit from
/// <see cref="Huia.Identity.PhoneUser"/>/<see cref="Huia.Identity.StandardUser"/> (not this class directly) to
/// add custom claims.
/// </summary>
public abstract class HuiaUser : IdentityUser
{
    /// <summary>
    /// The user's given (first) name.
    /// </summary>
    public string? FirstName { get; set; }

    /// <summary>
    /// The user's family (last) name.
    /// </summary>
    public string? LastName { get; set; }

    /// <summary>
    /// A URL to the user's avatar/profile picture — populated from an external sign-in provider's
    /// <c>picture</c> claim when the account was created that way (see
    /// <c>ExternalLoginConfirmationModel</c>); never set for a password-registered account. Not validated or
    /// re-fetched by Huia itself — treat it as untrusted, provider-supplied display data.
    /// </summary>
    public string? Picture { get; set; }

    /// <summary>
    /// <see cref="PhoneNumber"/> normalized to E.164 (e.g. <c>+15551234567</c>) by
    /// <c>Huia.Common.PhoneNumberValidator</c>, used to look up an account by phone number
    /// (<see cref="Huia.Stores.IHuiaPhoneNumberStore"/>). Denormalized and populated by application code, the
    /// same treatment ASP.NET Core Identity itself gives <c>NormalizedEmail</c>/<c>NormalizedUserName</c> —
    /// not computed by the database.
    /// </summary>
    public string? NormalizedPhoneNumber { get; set; }
}