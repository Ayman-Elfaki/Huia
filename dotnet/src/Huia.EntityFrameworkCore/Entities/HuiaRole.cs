using Microsoft.AspNetCore.Identity;

namespace Huia.EntityFrameworkCore.Entities;

/// <summary>
/// A role. Belongs to exactly one tenant via <see cref="TenantId"/>; role-name uniqueness is scoped to
/// that tenant by a composite index rather than the Identity global default.
/// </summary>
public class HuiaRole : IdentityRole<string>
{
    /// <summary>Creates a role with a generated identifier.</summary>
    public HuiaRole()
    {
        Id = Guid.NewGuid().ToString("N");
    }

    /// <summary>Creates a role with a generated identifier and the given name.</summary>
    /// <param name="roleName">The role name.</param>
    public HuiaRole(string roleName)
        : this()
    {
        Name = roleName;
    }

    /// <summary>The tenant this role belongs to.</summary>
    public string TenantId { get; set; } = string.Empty;
}
