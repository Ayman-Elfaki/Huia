using Huia.EntityFrameworkCore.Common;
using Huia.EntityFrameworkCore.Identity;
using Huia.EntityFrameworkCore.Keys;
using Huia.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.EntityFrameworkCore.Models;

namespace Huia.EntityFrameworkCore;

/// <summary>
/// Extensions for configuring Huia's EF Core entities on a <see cref="ModelBuilder"/>.
/// </summary>
public static class ModelBuilderExtensions
{
    /// <summary>
    /// Configures the <see cref="SigningKey"/> entity backing Huia's automatic key management. Call this
    /// from <c>OnModelCreating</c> on your own <see cref="DbContext"/> if it doesn't inherit
    /// <see cref="HuiaDbContext"/> — e.g. because it has its own base class. <see cref="HuiaDbContext{TUser,TRole}"/>
    /// calls this internally, so consumers using that base class don't need to call it themselves.
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
    /// Configures Huia's own user/role model — <typeparamref name="TUser"/>/<typeparamref name="TRole"/> plus
    /// the <see cref="HuiaUserRole"/>/<see cref="HuiaUserLogin"/>/<see cref="HuiaUserToken"/> join entities —
    /// under <c>Huia*</c>-prefixed tables (e.g. <c>HuiaUsers</c>). Call this from <c>OnModelCreating</c> on
    /// your own <see cref="DbContext"/> if it doesn't inherit <see cref="HuiaDbContext{TUser,TRole}"/>.
    /// <see cref="HuiaDbContext{TUser,TRole}"/> calls this internally, so consumers using that base class
    /// don't need to call it themselves.
    /// </summary>
    public static ModelBuilder UseHuiaIdentityModel<TUser, TRole>(this ModelBuilder builder)
        where TUser : HuiaUser
        where TRole : HuiaRole
    {
        builder.Entity<TUser>(user =>
        {
            user.ToTable("HuiaUsers");
            user.HasKey(u => u.Id);
            user.Property(u => u.ConcurrencyStamp).IsConcurrencyToken();
            user.HasIndex(u => u.NormalizedUserName).IsUnique();

            // Non-unique — uniqueness is enforced at the application layer (Register/ExternalLoginConfirmation),
            // not the database, since more than one account may legitimately share an email address today.
            user.HasIndex(u => u.NormalizedEmail);

            // Non-unique, mirroring NormalizedEmail's own treatment — uniqueness is enforced at the
            // application layer in PhoneLoginModel, not the database, since two accounts may legitimately
            // share a phone number (see IHuiaPhoneNumberStore and docs/passwordless.md's hybrid-auth security
            // considerations).
            user.HasIndex(u => u.NormalizedPhoneNumber);
        });

        builder.Entity<TRole>(role =>
        {
            role.ToTable("HuiaRoles");
            role.HasKey(r => r.Id);
            role.Property(r => r.ConcurrencyStamp).IsConcurrencyToken();
            role.HasIndex(r => r.NormalizedName).IsUnique();
        });

        builder.Entity<HuiaUserRole>(userRole =>
        {
            userRole.ToTable("HuiaUserRoles");
            userRole.HasKey(ur => new { ur.UserId, ur.RoleId });
        });

        builder.Entity<HuiaUserLogin>(userLogin =>
        {
            userLogin.ToTable("HuiaUserLogins");
            userLogin.HasKey(ul => new { ul.LoginProvider, ul.ProviderKey });
            userLogin.HasIndex(ul => ul.UserId);
        });

        builder.Entity<HuiaUserToken>(userToken =>
        {
            userToken.ToTable("HuiaUserTokens");
            userToken.HasKey(ut => new { ut.UserId, ut.Provider, ut.Name });
        });

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
