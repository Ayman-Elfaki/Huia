namespace Huia.Identity;

/// <summary>
/// A thin, Duende-<c>IMembershipAdmin</c>-inspired facade over <see cref="HuiaUserManager"/> and
/// <see cref="HuiaRoleManager"/> — a naming/discoverability convenience for role assignment and lockout,
/// composing existing manager members rather than introducing new store surface. Phase 2's Admin endpoints
/// can adopt this in place of calling the managers directly.
/// </summary>
public sealed class HuiaMembershipAdmin(HuiaUserManager userManager, HuiaRoleManager roleManager)
{
    /// <summary>Whether a role named <paramref name="role"/> exists.</summary>
    public Task<bool> RoleExistsAsync(string role) => roleManager.RoleExistsAsync(role);

    /// <summary>Adds <paramref name="user"/> to the named role.</summary>
    public Task<HuiaIdentityResult> AddUserToRoleAsync(HuiaUser user, string role) =>
        userManager.AddToRoleAsync(user, role);

    /// <summary>Removes <paramref name="user"/> from the named role.</summary>
    public Task<HuiaIdentityResult> RemoveUserFromRoleAsync(HuiaUser user, string role) =>
        userManager.RemoveFromRoleAsync(user, role);

    /// <summary>Reconciles <paramref name="user"/>'s role membership to exactly <paramref name="roles"/>,
    /// adding/removing as needed.</summary>
    public async Task<HuiaIdentityResult> SetUserRolesAsync(HuiaUser user, IReadOnlyCollection<string> roles)
    {
        var current = await userManager.GetRolesAsync(user).ConfigureAwait(false);
        var errors = new List<HuiaIdentityError>();

        foreach (var role in current.Except(roles, StringComparer.Ordinal))
        {
            var result = await userManager.RemoveFromRoleAsync(user, role).ConfigureAwait(false);
            errors.AddRange(result.Errors);
        }

        foreach (var role in roles.Except(current, StringComparer.Ordinal))
        {
            var result = await userManager.AddToRoleAsync(user, role).ConfigureAwait(false);
            errors.AddRange(result.Errors);
        }

        return errors.Count == 0 ? HuiaIdentityResult.Success : HuiaIdentityResult.Failed(errors);
    }

    /// <summary>Every user belonging to the named role.</summary>
    public async Task<IReadOnlyList<HuiaUser>> GetRoleMembersAsync(string role) =>
        [.. await userManager.GetUsersInRoleAsync(role).ConfigureAwait(false)];

    /// <summary>Locks or unlocks <paramref name="user"/> indefinitely.</summary>
    public async Task<HuiaIdentityResult> SetLockedAsync(HuiaUser user, bool locked)
    {
        await userManager.SetLockoutEnabledAsync(user, true).ConfigureAwait(false);
        return await userManager.SetLockoutEndDateAsync(user, locked ? DateTimeOffset.MaxValue : null)
            .ConfigureAwait(false);
    }
}
