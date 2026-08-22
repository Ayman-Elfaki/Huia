using Huia.Identity;

namespace Huia.Stores;

/// <summary>
/// Backs <see cref="HuiaRole"/> persistence. Replaces ASP.NET Core Identity's
/// <c>IRoleStore&lt;HuiaRole&gt;</c>.
/// </summary>
public interface IHuiaRoleStore
{
    /// <summary>Finds a role by id, or <see langword="null"/> if none exists.</summary>
    Task<HuiaRole?> FindByIdAsync(string roleId, CancellationToken cancellationToken);

    /// <summary>Finds a role by normalized name, or <see langword="null"/> if none exists.</summary>
    Task<HuiaRole?> FindByNormalizedNameAsync(string normalizedName, CancellationToken cancellationToken);

    /// <summary>Persists a newly created role.</summary>
    Task CreateAsync(HuiaRole role, CancellationToken cancellationToken);

    /// <summary>
    /// Persists changes to an existing role. Throws <see cref="HuiaConcurrencyException"/> if
    /// <see cref="HuiaRole.ConcurrencyStamp"/> no longer matches the persisted value.
    /// </summary>
    Task UpdateAsync(HuiaRole role, CancellationToken cancellationToken);

    /// <summary>Deletes a role.</summary>
    Task DeleteAsync(HuiaRole role, CancellationToken cancellationToken);

    /// <summary>
    /// A queryable view of every role, for admin listing endpoints — or <see langword="null"/> if the
    /// implementation doesn't support arbitrary queries.
    /// </summary>
    IQueryable<HuiaRole>? Roles { get; }
}
