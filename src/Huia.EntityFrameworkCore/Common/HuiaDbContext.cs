using Huia.EntityFrameworkCore.Identity;
using Huia.Identity;
using Microsoft.EntityFrameworkCore;

namespace Huia.EntityFrameworkCore.Common;

/// <summary>
/// Base <see cref="DbContext"/> wiring up Huia's own user/role storage, OpenIddict's EF Core stores, and
/// Huia's own key-management table. Inherit from this (or the non-generic <see cref="HuiaDbContext"/>)
/// instead of building your own <see cref="DbContext"/> from scratch.
/// </summary>
public abstract class HuiaDbContext<TUser, TRole>(DbContextOptions options)
    : DbContext(options)
    where TUser : HuiaUser
    where TRole : HuiaRole
{
    /// <summary>Every user.</summary>
    public DbSet<TUser> Users => Set<TUser>();

    /// <summary>Every role.</summary>
    public DbSet<TRole> Roles => Set<TRole>();

    /// <summary>Role membership.</summary>
    public DbSet<HuiaUserRole> UserRoles => Set<HuiaUserRole>();

    /// <summary>External (third-party) logins.</summary>
    public DbSet<HuiaUserLogin> UserLogins => Set<HuiaUserLogin>();

    /// <summary>Per-user named tokens (reset/confirmation/OTP validation state, authenticator key, recovery
    /// codes).</summary>
    public DbSet<HuiaUserToken> UserTokens => Set<HuiaUserToken>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.UseOpenIddict();
        builder.UseKeyManagement();
        builder.UseHuiaIdentityModel<TUser, TRole>();
        builder.UseHuiaOpenIddictTableNames();

        builder.Entity<TUser>(user =>
        {
            user.Property(u => u.FirstName).HasMaxLength(256);
            user.Property(u => u.LastName).HasMaxLength(256);
            user.Property(u => u.Picture).HasMaxLength(2048);
            user.Property(u => u.NormalizedPhoneNumber).HasMaxLength(64);
        });
    }
}

/// <summary>
/// Convenience non-generic <see cref="HuiaDbContext{TUser,TRole}"/> using the default Huia entities.
/// </summary>
public abstract class HuiaDbContext(DbContextOptions options) : HuiaDbContext<HuiaUser, HuiaRole>(options);
