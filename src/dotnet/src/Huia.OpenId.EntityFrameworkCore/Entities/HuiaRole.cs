namespace Huia.OpenId.EntityFrameworkCore.Entities;

/// <summary>
/// A role. Belongs to exactly one tenant via <see cref="TenantId"/>; role-name uniqueness is scoped to
/// that tenant by a composite index rather than the Identity global default.
/// </summary>
public class HuiaRole : Huia.EntityFrameworkCore.Entities.HuiaRole
{
    /// <summary>Creates a role with a generated identifier.</summary>
    public HuiaRole()
        : base()
    {
    }

    /// <summary>Creates a role with a generated identifier and the given name.</summary>
    /// <param name="roleName">The role name.</param>
    public HuiaRole(string roleName)
        : base(roleName)
    {
    }

    /// <summary>The tenant this role belongs to.</summary>
    public string TenantId { get; set; } = string.Empty;
}
