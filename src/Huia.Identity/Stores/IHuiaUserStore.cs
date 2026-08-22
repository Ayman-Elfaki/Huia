using Huia.Identity;

namespace Huia.Stores;

/// <summary>
/// Backs <see cref="HuiaUser"/> persistence — core CRUD only; logins, roles, and tokens are separate
/// capability interfaces (<see cref="IHuiaUserLoginStore"/>, <see cref="IHuiaUserRoleStore"/>,
/// <see cref="IHuiaUserTokenStore"/>) a single implementation typically implements alongside this one.
/// Replaces ASP.NET Core Identity's <c>IUserStore&lt;HuiaUser&gt;</c>.
/// </summary>
public interface IHuiaUserStore
{
    /// <summary>Finds a user by id, or <see langword="null"/> if none exists.</summary>
    Task<HuiaUser?> FindByIdAsync(string userId, CancellationToken cancellationToken);

    /// <summary>Finds a user by normalized username, or <see langword="null"/> if none exists.</summary>
    Task<HuiaUser?> FindByNormalizedUserNameAsync(string normalizedUserName, CancellationToken cancellationToken);

    /// <summary>Finds a user by normalized email, or <see langword="null"/> if none exists. More than one user
    /// may share an email — implementations return the first match.</summary>
    Task<HuiaUser?> FindByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken);

    /// <summary>Persists a newly created user.</summary>
    Task CreateAsync(HuiaUser user, CancellationToken cancellationToken);

    /// <summary>
    /// Persists changes to an existing user. Throws <see cref="HuiaConcurrencyException"/> if
    /// <see cref="HuiaUser.ConcurrencyStamp"/> no longer matches the persisted value (the row was modified
    /// concurrently since it was loaded).
    /// </summary>
    Task UpdateAsync(HuiaUser user, CancellationToken cancellationToken);

    /// <summary>Deletes a user.</summary>
    Task DeleteAsync(HuiaUser user, CancellationToken cancellationToken);

    /// <summary>
    /// A queryable view of every user, for admin listing endpoints — or <see langword="null"/> if the
    /// implementation doesn't support arbitrary queries (e.g. a key-value store).
    /// </summary>
    IQueryable<HuiaUser>? Users { get; }
}
