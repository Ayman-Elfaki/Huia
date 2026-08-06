using Huia.EntityFrameworkCore.Extensions;
using Huia.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Huia.EntityFrameworkCore.Common;

/// <summary>
/// Base <see cref="DbContext"/> wiring up ASP.NET Core Identity, OpenIddict's EF Core stores, and Huia's
/// own key-management table. Inherit from this (or the non-generic <see cref="HuiaDbContext"/>) instead of
/// <see cref="IdentityDbContext"/> directly.
/// </summary>
public abstract class HuiaDbContext<TUser, TRole>(DbContextOptions options)
    : IdentityDbContext<TUser, TRole, string>(options)
    where TUser : HuiaUser
    where TRole : HuiaRole
{
    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.UseOpenIddict();
        builder.UseKeyManagement();
        builder.UseHuiaIdentityTableNames<TUser, TRole, string>();
        builder.UseHuiaOpenIddictTableNames();

        builder.Entity<TUser>(user =>
        {
            user.Property(u => u.FirstName).HasMaxLength(256);
            user.Property(u => u.LastName).HasMaxLength(256);
        });
    }
}

/// <summary>
/// Convenience non-generic <see cref="HuiaDbContext{TUser,TRole}"/> using the default Huia entities.
/// </summary>
public abstract class HuiaDbContext(DbContextOptions options) : HuiaDbContext<HuiaUser, HuiaRole>(options);