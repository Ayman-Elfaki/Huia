using Huia.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Huia.EntityFrameworkCore.Common;

/// <summary>
/// Base <see cref="DbContext"/> wiring up ASP.NET Core Identity, OpenIddict's EF Core stores, and Huia's
/// own key-management table. Inherit from this (or the non-generic <see cref="HuiaDbContext"/>) instead of
/// <see cref="IdentityDbContext"/> directly.
/// </summary>
/// <remarks>
/// <see cref="HuiaUser"/> is abstract and mapped via EF Core's table-per-concrete-type (TPC) inheritance
/// strategy: every account is either a <typeparamref name="TPhoneUser"/> (own table, e.g. <c>HuiaPhoneUsers</c>)
/// or a <typeparamref name="TStandardUser"/> (own table, e.g. <c>HuiaUsers</c>) — see
/// <see cref="ModelBuilderExtensions.UseHuiaTpcUserHierarchy{TPhoneUser,TStandardUser,TRole,TKey}"/>.
/// <see cref="Microsoft.AspNetCore.Identity.UserManager{TUser}"/> stays bound to the abstract
/// <see cref="HuiaUser"/> base — nothing in ASP.NET Core Identity requires a concrete <c>TUser</c> — so
/// <c>UserManager&lt;HuiaUser&gt;</c> queries transparently span both tables.
/// </remarks>
public abstract class HuiaDbContext<TPhoneUser, TStandardUser, TRole>(DbContextOptions options)
    : IdentityDbContext<HuiaUser, TRole, string>(options)
    where TPhoneUser : PhoneUser
    where TStandardUser : StandardUser
    where TRole : HuiaRole
{
    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.UseOpenIddict();
        builder.UseKeyManagement();
        builder.UseHuiaTpcUserHierarchy<TPhoneUser, TStandardUser, TRole, string>();
        builder.UseHuiaOpenIddictTableNames();

        // Property/index configuration on the abstract base is replicated into each concrete leaf's own
        // table under TPC, so this applies once to cover both HuiaPhoneUsers and HuiaUsers.
        builder.Entity<HuiaUser>(user =>
        {
            user.Property(u => u.FirstName).HasMaxLength(256);
            user.Property(u => u.LastName).HasMaxLength(256);
            user.Property(u => u.Picture).HasMaxLength(2048);
            user.Property(u => u.NormalizedPhoneNumber).HasMaxLength(64);

            // Non-unique, mirroring NormalizedEmail's own treatment (ASP.NET Core Identity itself only
            // uniquely indexes NormalizedUserName) — uniqueness is enforced at the application layer in
            // PhoneLoginModel, not the database, since two accounts may legitimately share a phone number
            // (see IHuiaPhoneNumberStore and docs/passwordless.md's hybrid-auth security considerations).
            user.HasIndex(u => u.NormalizedPhoneNumber);
        });
    }
}

/// <summary>
/// Convenience non-generic <see cref="HuiaDbContext{TPhoneUser,TStandardUser,TRole}"/> using the default
/// Huia entities.
/// </summary>
public abstract class HuiaDbContext(DbContextOptions options)
    : HuiaDbContext<PhoneUser, StandardUser, HuiaRole>(options);