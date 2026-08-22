using Huia.Identity;

namespace Huia.Stores;

/// <summary>
/// Backs role membership for <see cref="HuiaUser"/>. Replaces ASP.NET Core Identity's
/// <c>IUserRoleStore&lt;HuiaUser&gt;</c>.
/// </summary>
public interface IHuiaUserRoleStore
{
    /// <summary>Adds <paramref name="user"/> to the role named <paramref name="roleName"/>.</summary>
    Task AddToRoleAsync(HuiaUser user, string roleName, CancellationToken cancellationToken);

    /// <summary>Removes <paramref name="user"/> from the role named <paramref name="roleName"/>.</summary>
    Task RemoveFromRoleAsync(HuiaUser user, string roleName, CancellationToken cancellationToken);

    /// <summary>Every role name <paramref name="user"/> belongs to.</summary>
    Task<IReadOnlyList<string>> GetRolesAsync(HuiaUser user, CancellationToken cancellationToken);

    /// <summary>Whether <paramref name="user"/> belongs to the role named <paramref name="roleName"/>.</summary>
    Task<bool> IsInRoleAsync(HuiaUser user, string roleName, CancellationToken cancellationToken);

    /// <summary>Every user belonging to the role named <paramref name="roleName"/>.</summary>
    Task<IReadOnlyList<HuiaUser>> GetUsersInRoleAsync(string roleName, CancellationToken cancellationToken);
}
