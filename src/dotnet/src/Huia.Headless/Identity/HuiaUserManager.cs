using Huia.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Huia.Headless.Identity;

/// <summary>The concrete Huia Headless <see cref="Huia.Identity.HuiaUserManager{TUser}"/>.</summary>
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

    /// <summary>Huia.Headless is single-tenant — the tenant id is fixed at startup, never per-account.</summary>
    protected override void OnUserCreating(HuiaUser user, string tenantId)
    {
        // Nothing to stamp: the common HuiaUser entity carries no TenantId column.
    }

    /// <summary>Finds an account by its phone number (the <c>PhoneNumber</c> column, not the username).</summary>
    public Task<HuiaUser?> FindByPhoneNumberAsync(string phoneNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);
        return Task.FromResult(Users.FirstOrDefault(u => u.PhoneNumber == phoneNumber));
    }
}
