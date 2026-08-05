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
}