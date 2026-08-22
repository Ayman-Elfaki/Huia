using Huia.EntityFrameworkCore.Common;
using Huia.EntityFrameworkCore.Keys;
using Huia.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
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
    /// Configures Huia's table-per-concrete-type (TPC) user hierarchy and renames ASP.NET Core Identity's
    /// default <c>AspNet*</c> tables (e.g. <c>AspNetUsers</c>) to Huia's <c>Huia*</c> equivalents. Call this
    /// from <c>OnModelCreating</c>, after Identity has configured its own model, on your own
    /// <see cref="DbContext"/> if it doesn't inherit <see cref="HuiaDbContext{TPhoneUser,TStandardUser,TRole}"/>.
    /// <see cref="HuiaDbContext{TPhoneUser,TStandardUser,TRole}"/> calls this internally, so consumers using
    /// that base class don't need to call it themselves. <typeparamref name="TKey"/> is the primary key type
    /// used by <see cref="HuiaUser"/>/<typeparamref name="TRole"/> — <see cref="string"/> for
    /// <see cref="HuiaUser"/>/<see cref="HuiaRole"/>, but any <see cref="IdentityUser"/>/
    /// <see cref="IdentityRole{TKey}"/> pair works.
    /// </summary>
    /// <remarks>
    /// TPC requires two mitigations beyond the mapping strategy itself, both validated against Sqlite,
    /// EF Core InMemory, and Npgsql:
    /// <list type="number">
    /// <item>
    /// ASP.NET Core Identity's own base <c>OnModelCreating</c> declares required foreign keys from
    /// <c>IdentityUserClaim</c>/<c>IdentityUserLogin</c>/<c>IdentityUserToken</c>/<c>IdentityUserRole</c> into
    /// the (now tableless, under TPC) abstract <see cref="HuiaUser"/> root — a single physical FK can't point
    /// at two independent leaf tables, so these are removed here; referential integrity for these four tables
    /// becomes application-layer, the same treatment <see cref="HuiaUser.NormalizedPhoneNumber"/>'s own index
    /// already gets. <see cref="Huia.EntityFrameworkCore.Identity.EfCoreHuiaUserStore{TContext}"/>'s
    /// <c>DeleteAsync</c> is what now performs the cleanup the dropped <c>ON DELETE CASCADE</c> used to do.
    /// </item>
    /// <item>
    /// Identity's base model also hardcodes index names (<c>"UserNameIndex"</c>, <c>"EmailIndex"</c>) on the
    /// abstract root. Under TPC that's one shared conceptual index across every leaf, not independent per-leaf
    /// metadata, so both leaf tables would render a <c>CREATE INDEX</c> with the identical name — a collision
    /// on any provider with a database/schema-wide index namespace (Sqlite, Postgres). The shared index is
    /// removed and a fresh, leaf-prefixed index is declared on each concrete leaf instead.
    /// </item>
    /// </list>
    /// </remarks>
    public static ModelBuilder UseHuiaTpcUserHierarchy<TPhoneUser, TStandardUser, TRole, TKey>(this ModelBuilder builder)
        where TPhoneUser : PhoneUser
        where TStandardUser : StandardUser
        where TRole : IdentityRole<TKey>
        where TKey : IEquatable<TKey>
    {
        // TPC: the abstract root gets no table of its own; each concrete leaf gets its own.
        builder.Entity<HuiaUser>().UseTpcMappingStrategy();
        builder.Entity<HuiaUser>().ToTable((string?)null);
        builder.Entity<TStandardUser>().ToTable("HuiaUsers");
        builder.Entity<TPhoneUser>().ToTable("HuiaPhoneUsers");

        builder.Entity<TRole>().ToTable("HuiaRoles");
        builder.Entity<IdentityUserRole<TKey>>().ToTable("HuiaUserRoles");
        builder.Entity<IdentityUserClaim<TKey>>().ToTable("HuiaUserClaims");
        builder.Entity<IdentityUserLogin<TKey>>().ToTable("HuiaUserLogins");
        builder.Entity<IdentityRoleClaim<TKey>>().ToTable("HuiaRoleClaims");
        builder.Entity<IdentityUserToken<TKey>>().ToTable("HuiaUserTokens");

        foreach (var dependentType in new[]
                 {
                     typeof(IdentityUserClaim<TKey>), typeof(IdentityUserLogin<TKey>),
                     typeof(IdentityUserToken<TKey>), typeof(IdentityUserRole<TKey>),
                 })
        {
            var entityType = builder.Model.FindEntityType(dependentType)!;
            foreach (var fk in entityType.GetForeignKeys()
                         .Where(fk => fk.PrincipalEntityType.ClrType == typeof(HuiaUser)).ToList())
            {
                entityType.RemoveForeignKey(fk.Properties, fk.PrincipalKey, fk.PrincipalEntityType);
            }
        }

        var baseUserType = builder.Model.FindEntityType(typeof(HuiaUser))!;
        foreach (var index in baseUserType.GetDeclaredIndexes().ToList())
        {
            baseUserType.RemoveIndex(index);
        }

        builder.Entity<TStandardUser>().HasIndex(u => u.NormalizedUserName).HasDatabaseName("StandardUser_UserNameIndex").IsUnique();
        builder.Entity<TStandardUser>().HasIndex(u => u.NormalizedEmail).HasDatabaseName("StandardUser_EmailIndex");
        builder.Entity<TPhoneUser>().HasIndex(u => u.NormalizedUserName).HasDatabaseName("PhoneUser_UserNameIndex").IsUnique();
        builder.Entity<TPhoneUser>().HasIndex(u => u.NormalizedEmail).HasDatabaseName("PhoneUser_EmailIndex");

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