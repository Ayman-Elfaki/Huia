using Huia.OpenId.Entities;
using Microsoft.AspNetCore.Identity;
using Huia.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Huia.OpenId.Identity;

/// <summary>The concrete Huia OpenId <see cref="Huia.Identity.HuiaUserManager{TUser}"/>.</summary>
/// <remarks>Creates the manager. Parameters are the stock <see cref="UserManager{TUser}"/> dependencies.</remarks>
public class HuiaUserManager(
    IUserStore<HuiaUser> store,
    IOptions<IdentityOptions> optionsAccessor,
    IPasswordHasher<HuiaUser> passwordHasher,
    IEnumerable<IUserValidator<HuiaUser>> userValidators,
    IEnumerable<IPasswordValidator<HuiaUser>> passwordValidators,
    ILookupNormalizer keyNormalizer,
    IdentityErrorDescriber errors,
    IServiceProvider services,
    ILogger<UserManager<HuiaUser>> logger) : Huia.Identity.HuiaUserManager<HuiaUser>(store, optionsAccessor,
    passwordHasher, userValidators, passwordValidators, keyNormalizer, errors, services, logger)
{
    private readonly IServiceProvider _services = services;

    /// <summary>Stamps the tenant a freshly created phone/external-login account belongs to.</summary>
    protected override void OnUserCreating(HuiaUser user, string tenantId) => user.TenantId = tenantId;

    /// <summary>
    /// Finds an account by its phone number (the <c>PhoneNumber</c> column, not the username). Scoped
    /// to the current tenant by the store's global query filter.
    /// </summary>
    public Task<HuiaUser?> FindByPhoneNumberAsync(string phoneNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);
        return _services.GetRequiredService<IHuiaPhoneLookupStore>().FindByPhoneNumberAsync(phoneNumber, CancellationToken);
    }
}
