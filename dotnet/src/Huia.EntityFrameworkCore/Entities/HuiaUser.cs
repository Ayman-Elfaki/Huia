using Microsoft.AspNetCore.Identity;

namespace Huia.EntityFrameworkCore.Entities;

/// <summary>
/// A user account. Belongs to exactly one tenant via <see cref="TenantId"/>; uniqueness of the user name
/// and email is scoped to that tenant by composite indexes rather than the Identity global defaults.
/// </summary>
public class HuiaUser : IdentityUser<string>
{
    /// <summary>Creates a user with a generated identifier.</summary>
    public HuiaUser()
    {
        Id = Guid.NewGuid().ToString("N");
    }

    /// <summary>The tenant this user belongs to. Never changes for the lifetime of the account.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>The user's given name. Required by the account UI before a profile is considered complete.</summary>
    public string FirstName { get; set; } = string.Empty;

    /// <summary>The user's family name. Required by the account UI before a profile is considered complete.</summary>
    public string LastName { get; set; } = string.Empty;

    /// <summary>When the account was created (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Whether both <see cref="FirstName"/> and <see cref="LastName"/> have been supplied.</summary>
    public bool HasCompleteProfile =>
        !string.IsNullOrWhiteSpace(FirstName) && !string.IsNullOrWhiteSpace(LastName);
}
