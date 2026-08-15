using Microsoft.AspNetCore.Identity;

namespace Huia.Identity;

/// <summary>
/// Default ASP.NET Core Identity user type used by Huia.
/// Inherit from this to add custom claims.
/// </summary>
public class HuiaUser : IdentityUser
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

    /// <summary>
    /// Whether <see cref="PhoneNumber"/> alone (proven via OTP) is sufficient to sign into this account.
    /// <c>false</c> by default even when <see cref="PhoneNumber"/> is set — e.g. a number recorded for a
    /// future SMS-2FA feature or by an admin doesn't implicitly become a standalone sign-in path. Only
    /// accounts created through <c>huia.Authentication.UsePasswordlessFlow()</c>'s phone sign-in flow set
    /// this to <c>true</c>. See docs/passwordless.md's hybrid-auth security considerations for why this
    /// isn't inferred from <see cref="PhoneNumber"/>/<see cref="IdentityUser.PhoneNumberConfirmed"/> alone.
    /// </summary>
    public bool PasswordlessLoginEnabled { get; set; }
}