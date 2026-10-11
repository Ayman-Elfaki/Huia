using Huia.OpenId.Entities;

namespace Huia.Stores;

/// <summary>
/// Asynchronous, tenant-scoped lookup of an OpenId account by its phone number. ASP.NET Core Identity has no
/// find-by-phone operation, and an <c>IQueryable</c> cannot be awaited without a provider-specific package,
/// so the persistence adapter supplies it.
/// </summary>
public interface IHuiaPhoneLookupStore
{
    /// <summary>
    /// Finds the account whose <c>PhoneNumber</c> equals <paramref name="phoneNumber"/> within the tenant
    /// currently in scope, as a tracked entity ready to be updated. Returns <c>null</c> when none exists.
    /// </summary>
    Task<HuiaUser?> FindByPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default);
}
