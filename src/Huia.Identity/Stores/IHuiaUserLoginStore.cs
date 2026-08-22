using Huia.Identity;

namespace Huia.Stores;

/// <summary>
/// Backs external (third-party) sign-in providers registered via
/// <c>huia.Authentication.UseExternalAuthenticationFlow(...)</c>. Replaces ASP.NET Core Identity's
/// <c>IUserLoginStore&lt;HuiaUser&gt;</c>.
/// </summary>
public interface IHuiaUserLoginStore
{
    /// <summary>Links <paramref name="login"/> to <paramref name="user"/>.</summary>
    Task AddLoginAsync(HuiaUser user, HuiaUserLoginInfo login, CancellationToken cancellationToken);

    /// <summary>Unlinks the login identified by <paramref name="loginProvider"/>/<paramref name="providerKey"/>
    /// from <paramref name="user"/>.</summary>
    Task RemoveLoginAsync(HuiaUser user, string loginProvider, string providerKey, CancellationToken cancellationToken);

    /// <summary>Every external login linked to <paramref name="user"/>.</summary>
    Task<IReadOnlyList<HuiaUserLoginInfo>> GetLoginsAsync(HuiaUser user, CancellationToken cancellationToken);

    /// <summary>Finds the user linked to the given external login, or <see langword="null"/> if none exists.</summary>
    Task<HuiaUser?> FindByLoginAsync(string loginProvider, string providerKey, CancellationToken cancellationToken);
}
