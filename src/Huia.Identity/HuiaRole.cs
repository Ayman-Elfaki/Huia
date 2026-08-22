namespace Huia.Identity;

/// <summary>
/// Huia's own role type — no longer inherits ASP.NET Core Identity's <c>IdentityRole</c>.
/// </summary>
public class HuiaRole
{
    /// <summary>The role's unique id.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>The role's name.</summary>
    public string? Name { get; set; }

    /// <summary><see cref="Name"/>, normalized (upper-invariant) for case-insensitive lookup.</summary>
    public string? NormalizedName { get; set; }

    /// <summary>Optimistic-concurrency token, regenerated on every update.</summary>
    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Creates an empty role.</summary>
    public HuiaRole()
    {
    }

    /// <summary>Creates a role named <paramref name="roleName"/>.</summary>
    public HuiaRole(string roleName) => Name = roleName;

    /// <inheritdoc />
    public override string ToString() => Name ?? string.Empty;
}
