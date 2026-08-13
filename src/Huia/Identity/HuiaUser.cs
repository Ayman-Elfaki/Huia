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
}