using Huia.Identity;

namespace Huia.Stores;

/// <summary>
/// Looks up a <see cref="HuiaUser"/> by normalized (E.164) phone number. Required by
/// <c>huia.Authentication.UsePasswordlessFlow(...)</c> — <c>.WithEntityFrameworkStores&lt;TContext&gt;()</c>
/// registers an implementation automatically; a fully custom (non-EF) store implements this directly, since
/// <see cref="IHuiaStore{TApplication,TAuthorization,TScope,TToken}"/> composes it.
/// </summary>
public interface IHuiaPhoneNumberStore
{
    /// <summary>
    /// Returns the user whose <see cref="HuiaUser.NormalizedPhoneNumber"/> equals
    /// <paramref name="normalizedPhoneNumber"/>, or <see langword="null"/> if none exists. More than one
    /// user may share a phone number (see docs/passwordless.md) — implementations return the first match;
    /// callers must additionally check whether the result <see langword="is"/> <see cref="Huia.Identity.PhoneUser"/>
    /// before treating it as a passwordless-reachable account (a <see cref="Huia.Identity.StandardUser"/> may
    /// have a phone number recorded for account-management purposes without being one).
    /// </summary>
    Task<HuiaUser?> FindByNormalizedPhoneNumberAsync(string normalizedPhoneNumber, CancellationToken cancellationToken);
}
