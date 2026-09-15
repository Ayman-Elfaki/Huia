using Microsoft.AspNetCore.Identity;

namespace Huia.Entities;

/// <summary>
/// A user account. Not multi-tenant — <c>Huia.OpenId.EntityFrameworkCore.Entities.HuiaUser</c> subclasses
/// this to add a <c>TenantId</c> for the multi-tenant flavor; a single-tenant host uses this type directly.
/// </summary>
public class HuiaUser : IdentityUser<string>
{
    /// <summary>Creates a user with a generated identifier.</summary>
    public HuiaUser()
    {
        Id = Guid.NewGuid().ToString("N");
    }

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
