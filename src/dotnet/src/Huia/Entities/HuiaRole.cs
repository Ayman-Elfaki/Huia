using Microsoft.AspNetCore.Identity;

namespace Huia.Entities;

/// <summary>
/// A role. Not multi-tenant — <c>Huia.OpenId.EntityFrameworkCore.Entities.HuiaRole</c> subclasses this to
/// add a <c>TenantId</c> for the multi-tenant flavor; a single-tenant host uses this type directly.
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

    /// <summary>
    /// Whether this role was declared in code (<c>TenantOptions.AddRoles</c>, seeded by the role seeder and
    /// therefore read-only — cannot be renamed or deleted through the admin API) or created at runtime
    /// through the admin API. One of <see cref="HuiaConstants.Origins.Static"/> or
    /// <see cref="HuiaConstants.Origins.Dynamic"/>.
    /// </summary>
    public string Origin { get; set; } = HuiaConstants.Origins.Dynamic;
}
