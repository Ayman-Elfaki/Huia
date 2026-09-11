using Huia.OpenId.EntityFrameworkCore.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Huia.OpenId.Identity;

/// <summary>The concrete Huia OpenId <see cref="Huia.Identity.HuiaUserManager{TUser}"/>.</summary>
public class HuiaUserManager : Huia.Identity.HuiaUserManager<HuiaUser>
{
    /// <summary>Creates the manager. Parameters are the stock <see cref="UserManager{TUser}"/> dependencies.</summary>
    public HuiaUserManager(
        IUserStore<HuiaUser> store,
        IOptions<IdentityOptions> optionsAccessor,
        IPasswordHasher<HuiaUser> passwordHasher,
        IEnumerable<IUserValidator<HuiaUser>> userValidators,
        IEnumerable<IPasswordValidator<HuiaUser>> passwordValidators,
        ILookupNormalizer keyNormalizer,
        IdentityErrorDescriber errors,
        IServiceProvider services,
        ILogger<UserManager<HuiaUser>> logger)
        : base(store, optionsAccessor, passwordHasher, userValidators, passwordValidators, keyNormalizer, errors, services, logger)
    {
    }

    /// <summary>Stamps the tenant a freshly created phone/external-login account belongs to.</summary>
    protected override void OnUserCreating(HuiaUser user, string tenantId) => user.TenantId = tenantId;

    /// <summary>
    /// Finds an account by its phone number (the <c>PhoneNumber</c> column, not the username). Scoped
    /// to the current tenant by <c>HuiaDbContext</c>'s global query filter.
    /// </summary>
    public Task<HuiaUser?> FindByPhoneNumberAsync(string phoneNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);
        return Users.FirstOrDefaultAsync(u => u.PhoneNumber == phoneNumber, CancellationToken);
    }
}
