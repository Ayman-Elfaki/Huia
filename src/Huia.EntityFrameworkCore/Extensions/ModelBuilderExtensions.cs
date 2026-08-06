using Huia.EntityFrameworkCore.Common;
using Huia.EntityFrameworkCore.Keys;
using Huia.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.EntityFrameworkCore.Models;

namespace Huia.EntityFrameworkCore.Extensions;

/// <summary>
/// Extensions for configuring Huia's EF Core entities on a <see cref="ModelBuilder"/>.
/// </summary>
public static class ModelBuilderExtensions
{
    /// <summary>
    /// Configures the <see cref="SigningKey"/> entity backing Huia's automatic key management. Call this
    /// from <c>OnModelCreating</c> on your own <see cref="DbContext"/> if it doesn't inherit
    /// <see cref="HuiaDbContext"/> — e.g. because it already inherits
    /// <see cref="Microsoft.AspNetCore.Identity.EntityFrameworkCore.IdentityDbContext"/>
    /// directly or has its own base class. <see cref="HuiaDbContext{TUser,TRole}"/> calls this internally, so
    /// consumers using that base class don't need to call it themselves.
    /// </summary>
    public static ModelBuilder UseKeyManagement(this ModelBuilder builder)
    {
        builder.Entity<SigningKey>(key =>
        {
            key.ToTable("HuiaSigningKeys");
            key.HasKey(k => k.Id);
            key.Property(k => k.Usage).HasConversion<int>();
            key.HasIndex(k => new { k.Usage, k.ExpiresAt });
            key.Property(k => k.ProtectedPrivateKey).IsRequired();
        });

        return builder;
    }

    /// <summary>
    /// Renames ASP.NET Core Identity's default <c>AspNet*</c> tables (e.g. <c>AspNetUsers</c>) to Huia's
    /// <c>Huia*</c> equivalents (e.g. <c>HuiaUsers</c>). Call this from <c>OnModelCreating</c>, after Identity
    /// has configured its own model, on your own <see cref="DbContext"/> if it doesn't inherit
    /// <see cref="HuiaDbContext{TUser,TRole}"/>. <see cref="HuiaDbContext{TUser,TRole}"/> calls this internally, so
    /// consumers using that base class don't need to call it themselves. <typeparamref name="TKey"/> is the
    /// primary key type used by <typeparamref name="TUser"/>/<typeparamref name="TRole"/> — <see cref="string"/>
    /// for <see cref="HuiaUser"/>/<see cref="HuiaRole"/>, but any <see cref="IdentityUser"/>/
    /// <see cref="IdentityRole{TKey}"/> pair works.
    /// </summary>
    public static ModelBuilder UseHuiaIdentityTableNames<TUser, TRole, TKey>(this ModelBuilder builder)
        where TUser : IdentityUser<TKey>
        where TRole : IdentityRole<TKey>
        where TKey : IEquatable<TKey>
    {
        builder.Entity<TUser>().ToTable("HuiaUsers");
        builder.Entity<TRole>().ToTable("HuiaRoles");
        builder.Entity<IdentityUserRole<TKey>>().ToTable("HuiaUserRoles");
        builder.Entity<IdentityUserClaim<TKey>>().ToTable("HuiaUserClaims");
        builder.Entity<IdentityUserLogin<TKey>>().ToTable("HuiaUserLogins");
        builder.Entity<IdentityRoleClaim<TKey>>().ToTable("HuiaRoleClaims");
        builder.Entity<IdentityUserToken<TKey>>().ToTable("HuiaUserTokens");

        return builder;
    }

    /// <summary>
    /// Renames OpenIddict's default <c>OpenIddict*</c> tables (e.g. <c>OpenIddictApplications</c>) to
    /// Huia-prefixed equivalents (e.g. <c>HuiaApplications</c>). Call this from
    /// <c>OnModelCreating</c>, after <c>builder.UseOpenIddict()</c> has configured OpenIddict's own model,
    /// on your own <see cref="DbContext"/> if it doesn't inherit
    /// <see cref="HuiaDbContext"/>. <see cref="HuiaDbContext{TUser,TRole}"/> calls this internally, so
    /// consumers using that base class don't need to call it themselves.
    /// </summary>
    public static ModelBuilder UseHuiaOpenIddictTableNames(this ModelBuilder builder)
    {
        builder.Entity<OpenIddictEntityFrameworkCoreApplication>().ToTable("HuiaApplications");
        builder.Entity<OpenIddictEntityFrameworkCoreAuthorization>().ToTable("HuiaAuthorizations");
        builder.Entity<OpenIddictEntityFrameworkCoreScope>().ToTable("HuiaScopes");
        builder.Entity<OpenIddictEntityFrameworkCoreToken>().ToTable("HuiaTokens");

        return builder;
    }
}