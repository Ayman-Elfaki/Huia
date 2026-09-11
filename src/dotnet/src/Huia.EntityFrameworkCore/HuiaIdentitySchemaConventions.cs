using Huia.EntityFrameworkCore.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Huia.EntityFrameworkCore;

/// <summary>
/// The <c>Huia*</c>-renamed ASP.NET Core Identity schema, shared by every Huia identity-provider flavor.
/// Multi-tenant table setup (Finbuckle) and OpenIddict's own tables are layered on separately by
/// <c>Huia.OpenId.EntityFrameworkCore.HuiaDbContext</c> — this convention only touches the plain Identity
/// tables and the built-in passkey (WebAuthn credential) entity.
/// </summary>
public static class HuiaIdentitySchemaConventions
{
    /// <summary>
    /// Renames every Identity table to a <c>Huia*</c> name (there are no <c>AspNet*</c> tables in the
    /// database) and configures the passkey entity deterministically, whether or not the base Identity
    /// model builder already mapped it (it does so only when <c>IdentityOptions.Stores.SchemaVersion</c>
    /// resolves to <c>Version3</c> — true on the DI path, not when the context is constructed directly by
    /// a model test or a design-time factory).
    /// </summary>
    /// <param name="builder">The model builder, inside <c>OnModelCreating</c>.</param>
    /// <typeparam name="TUser">The concrete user entity, at least as derived as <see cref="HuiaUser"/>.</typeparam>
    /// <typeparam name="TRole">The concrete role entity, at least as derived as <see cref="HuiaRole"/>.</typeparam>
    /// <returns>The same builder, for chaining.</returns>
    public static ModelBuilder ConfigureHuiaIdentitySchema<TUser, TRole>(this ModelBuilder builder)
        where TUser : HuiaUser
        where TRole : HuiaRole
    {
        builder.Entity<TUser>().ToTable("HuiaUsers");
        builder.Entity<TRole>().ToTable("HuiaRoles");
        builder.Entity<IdentityUserRole<string>>().ToTable("HuiaUserRoles");
        builder.Entity<IdentityUserClaim<string>>().ToTable("HuiaUserClaims");
        builder.Entity<IdentityUserLogin<string>>().ToTable("HuiaUserLogins");
        builder.Entity<IdentityUserToken<string>>().ToTable("HuiaUserTokens");
        builder.Entity<IdentityRoleClaim<string>>().ToTable("HuiaRoleClaims");

        builder.Entity<TUser>(b =>
        {
            b.Property(u => u.FirstName).HasMaxLength(256);
            b.Property(u => u.LastName).HasMaxLength(256);
        });

        builder.Entity<TRole>().Property(r => r.Origin).HasMaxLength(16).IsRequired();

        var passkey = builder.Entity<IdentityUserPasskey<string>>();
        passkey.ToTable("HuiaUserPasskeys");
        passkey.HasKey(p => p.CredentialId);
        passkey.Property(p => p.CredentialId).HasMaxLength(1024);
        passkey.OwnsOne(p => p.Data, owned => owned.ToJson());

        builder.Entity<TUser>()
            .HasMany<IdentityUserPasskey<string>>()
            .WithOne()
            .HasForeignKey(p => p.UserId)
            .IsRequired();

        return builder;
    }
}
