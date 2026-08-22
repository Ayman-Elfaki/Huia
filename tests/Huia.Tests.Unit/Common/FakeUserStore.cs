using Huia.Identity;
using Huia.Stores;

namespace Huia.Tests.Unit.Common;

/// <summary>
/// Minimal in-memory <see cref="IHuiaUserStore"/>/<see cref="IHuiaUserRoleStore"/> backing a single
/// <see cref="HuiaUser"/>, just enough for a real <see cref="HuiaUserManager"/> to serve the reads
/// <see cref="Huia.Common.ClaimsUtils.CreateUserIdentityAsync"/> makes (id/username/email/roles) without a
/// database.
/// </summary>
internal sealed class FakeUserStore(HuiaUser user, IReadOnlyList<string> roles) :
    IHuiaUserStore, IHuiaUserRoleStore
{
    public Task<HuiaUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
        Task.FromResult(string.Equals(userId, user.Id, StringComparison.Ordinal) ? user : null);

    public Task<HuiaUser?> FindByNormalizedUserNameAsync(string normalizedUserName, CancellationToken cancellationToken) =>
        Task.FromResult(string.Equals(normalizedUserName, user.NormalizedUserName, StringComparison.Ordinal)
            ? user
            : null);

    public Task<HuiaUser?> FindByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        Task.FromResult(string.Equals(normalizedEmail, user.NormalizedEmail, StringComparison.Ordinal)
            ? user
            : null);

    public Task CreateAsync(HuiaUser u, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task UpdateAsync(HuiaUser u, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task DeleteAsync(HuiaUser u, CancellationToken cancellationToken) => Task.CompletedTask;

    public IQueryable<HuiaUser>? Users => null;

    public Task AddToRoleAsync(HuiaUser u, string roleName, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Not needed for these tests — roles are seeded via the constructor.");

    public Task RemoveFromRoleAsync(HuiaUser u, string roleName, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Not needed for these tests — roles are seeded via the constructor.");

    public Task<IReadOnlyList<string>> GetRolesAsync(HuiaUser u, CancellationToken cancellationToken) =>
        Task.FromResult(roles);

    public Task<bool> IsInRoleAsync(HuiaUser u, string roleName, CancellationToken cancellationToken) =>
        Task.FromResult(roles.Contains(roleName, StringComparer.Ordinal));

    public Task<IReadOnlyList<HuiaUser>> GetUsersInRoleAsync(string roleName, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<HuiaUser>>(roles.Contains(roleName, StringComparer.Ordinal) ? [user] : []);
}
