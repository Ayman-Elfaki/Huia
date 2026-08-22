using Huia.Stores;

namespace Huia.Identity;

/// <summary>
/// Huia's own role manager — replaces ASP.NET Core Identity's <c>RoleManager&lt;HuiaRole&gt;</c>, kept
/// name/shape-compatible with it so existing call sites migrate by swapping the type.
/// </summary>
public class HuiaRoleManager(IHuiaRoleStore store, HuiaErrorDescriber errorDescriber)
{
    private static string Normalize(string value) => value.Trim().ToUpperInvariant();

    /// <summary>A queryable view of every role, or <see langword="null"/> if the store doesn't support it.</summary>
    public IQueryable<HuiaRole>? Roles => store.Roles;

    /// <summary>Whether <see cref="Roles"/> is usable.</summary>
    public bool SupportsQueryableRoles => store.Roles is not null;

    /// <summary>Finds a role by id, or <see langword="null"/> if none exists.</summary>
    public Task<HuiaRole?> FindByIdAsync(string roleId) => store.FindByIdAsync(roleId, CancellationToken.None);

    /// <summary>Finds a role by name (case-insensitive), or <see langword="null"/> if none exists.</summary>
    public Task<HuiaRole?> FindByNameAsync(string roleName) =>
        store.FindByNormalizedNameAsync(Normalize(roleName), CancellationToken.None);

    /// <summary>Whether a role named <paramref name="roleName"/> exists.</summary>
    public async Task<bool> RoleExistsAsync(string roleName) =>
        await FindByNameAsync(roleName).ConfigureAwait(false) is not null;

    /// <summary>Creates a role.</summary>
    public async Task<HuiaIdentityResult> CreateAsync(HuiaRole role)
    {
        if (string.IsNullOrWhiteSpace(role.Name))
        {
            return HuiaIdentityResult.Failed(errorDescriber.InvalidRoleName(role.Name));
        }

        role.NormalizedName = Normalize(role.Name);

        var existing = await store.FindByNormalizedNameAsync(role.NormalizedName, CancellationToken.None)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return HuiaIdentityResult.Failed(errorDescriber.DuplicateRoleName(role.Name));
        }

        try
        {
            await store.CreateAsync(role, CancellationToken.None).ConfigureAwait(false);
        }
        catch (HuiaConcurrencyException)
        {
            return HuiaIdentityResult.Failed(errorDescriber.ConcurrencyFailure());
        }

        return HuiaIdentityResult.Success;
    }

    /// <summary>Persists changes to an existing role, regenerating its concurrency stamp.</summary>
    public async Task<HuiaIdentityResult> UpdateAsync(HuiaRole role)
    {
        if (string.IsNullOrWhiteSpace(role.Name))
        {
            return HuiaIdentityResult.Failed(errorDescriber.InvalidRoleName(role.Name));
        }

        role.NormalizedName = Normalize(role.Name);
        role.ConcurrencyStamp = Guid.NewGuid().ToString();

        try
        {
            await store.UpdateAsync(role, CancellationToken.None).ConfigureAwait(false);
        }
        catch (HuiaConcurrencyException)
        {
            return HuiaIdentityResult.Failed(errorDescriber.ConcurrencyFailure());
        }

        return HuiaIdentityResult.Success;
    }

    /// <summary>Deletes a role.</summary>
    public async Task<HuiaIdentityResult> DeleteAsync(HuiaRole role)
    {
        try
        {
            await store.DeleteAsync(role, CancellationToken.None).ConfigureAwait(false);
        }
        catch (HuiaConcurrencyException)
        {
            return HuiaIdentityResult.Failed(errorDescriber.ConcurrencyFailure());
        }

        return HuiaIdentityResult.Success;
    }
}
