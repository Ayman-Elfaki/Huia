namespace Huia.OpenId.EntityFrameworkCore.Entities;

/// <summary>
/// A user account. Belongs to exactly one tenant via <see cref="TenantId"/>; uniqueness of the user name
/// and email is scoped to that tenant by composite indexes rather than the Identity global defaults.
/// </summary>
public class HuiaUser : Huia.Entities.HuiaUser
{
    /// <summary>The tenant this user belongs to. Never changes for the lifetime of the account.</summary>
    public string TenantId { get; set; } = string.Empty;
}
