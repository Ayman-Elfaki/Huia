using Huia.Identity;
using Microsoft.AspNetCore.Identity;

namespace Huia.Tests.Unit.Common;

/// <summary>
/// Minimal in-memory <see cref="IUserStore{HuiaUser}"/>/<see cref="IUserEmailStore{HuiaUser}"/>/
/// <see cref="IUserRoleStore{HuiaUser}"/> backing a single <see cref="HuiaUser"/>, just enough for a real
/// <see cref="UserManager{HuiaUser}"/> to serve the reads <see cref="Huia.Common.ClaimsUtils.CreateUserIdentityAsync"/>
/// makes (id/username/email/roles) without a database.
/// </summary>
internal sealed class FakeUserStore(HuiaUser user, IReadOnlyList<string> roles) :
    IUserStore<HuiaUser>, IUserEmailStore<HuiaUser>, IUserRoleStore<HuiaUser>
{
    public Task<string> GetUserIdAsync(HuiaUser u, CancellationToken ct) => Task.FromResult(u.Id);

    public Task<string?> GetUserNameAsync(HuiaUser u, CancellationToken ct) => Task.FromResult(u.UserName);

    public Task SetUserNameAsync(HuiaUser u, string? userName, CancellationToken ct)
    {
        u.UserName = userName;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedUserNameAsync(HuiaUser u, CancellationToken ct) =>
        Task.FromResult(u.NormalizedUserName);

    public Task SetNormalizedUserNameAsync(HuiaUser u, string? normalizedName, CancellationToken ct)
    {
        u.NormalizedUserName = normalizedName;
        return Task.CompletedTask;
    }

    public Task<IdentityResult> CreateAsync(HuiaUser u, CancellationToken ct) =>
        Task.FromResult(IdentityResult.Success);

    public Task<IdentityResult> UpdateAsync(HuiaUser u, CancellationToken ct) =>
        Task.FromResult(IdentityResult.Success);

    public Task<IdentityResult> DeleteAsync(HuiaUser u, CancellationToken ct) =>
        Task.FromResult(IdentityResult.Success);

    public Task<HuiaUser?> FindByIdAsync(string userId, CancellationToken ct) =>
        Task.FromResult(string.Equals(userId, user.Id, StringComparison.Ordinal) ? user : null);

    public Task<HuiaUser?> FindByNameAsync(string normalizedUserName, CancellationToken ct) =>
        Task.FromResult(string.Equals(normalizedUserName, user.NormalizedUserName, StringComparison.Ordinal)
            ? user
            : null);

    public Task SetEmailAsync(HuiaUser u, string? email, CancellationToken ct)
    {
        u.Email = email;
        return Task.CompletedTask;
    }

    public Task<string?> GetEmailAsync(HuiaUser u, CancellationToken ct) => Task.FromResult(u.Email);

    public Task<bool> GetEmailConfirmedAsync(HuiaUser u, CancellationToken ct) =>
        Task.FromResult(u.EmailConfirmed);

    public Task SetEmailConfirmedAsync(HuiaUser u, bool confirmed, CancellationToken ct)
    {
        u.EmailConfirmed = confirmed;
        return Task.CompletedTask;
    }

    public Task<HuiaUser?> FindByEmailAsync(string normalizedEmail, CancellationToken ct) =>
        Task.FromResult(string.Equals(normalizedEmail, user.NormalizedEmail, StringComparison.Ordinal)
            ? user
            : null);

    public Task<string?> GetNormalizedEmailAsync(HuiaUser u, CancellationToken ct) =>
        Task.FromResult(u.NormalizedEmail);

    public Task SetNormalizedEmailAsync(HuiaUser u, string? normalizedEmail, CancellationToken ct)
    {
        u.NormalizedEmail = normalizedEmail;
        return Task.CompletedTask;
    }

    public Task AddToRoleAsync(HuiaUser u, string roleName, CancellationToken ct) =>
        throw new NotSupportedException("Not needed for these tests — roles are seeded via the constructor.");

    public Task RemoveFromRoleAsync(HuiaUser u, string roleName, CancellationToken ct) =>
        throw new NotSupportedException("Not needed for these tests — roles are seeded via the constructor.");

    public Task<IList<string>> GetRolesAsync(HuiaUser u, CancellationToken ct) =>
        Task.FromResult<IList<string>>([.. roles]);

    public Task<bool> IsInRoleAsync(HuiaUser u, string roleName, CancellationToken ct) =>
        Task.FromResult(roles.Contains(roleName, StringComparer.Ordinal));

    public Task<IList<HuiaUser>> GetUsersInRoleAsync(string roleName, CancellationToken ct) =>
        Task.FromResult<IList<HuiaUser>>(roles.Contains(roleName, StringComparer.Ordinal) ? [user] : []);

    public void Dispose()
    {
    }
}
