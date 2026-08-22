namespace Huia.EntityFrameworkCore.Identity;

/// <summary>Join entity backing role membership — the EF Core-mapped form of <see cref="Huia.Stores.IHuiaUserRoleStore"/>.
/// Composite key (<see cref="UserId"/>, <see cref="RoleId"/>).</summary>
public sealed class HuiaUserRole
{
    /// <summary>The user's id.</summary>
    public required string UserId { get; set; }

    /// <summary>The role's id.</summary>
    public required string RoleId { get; set; }
}
