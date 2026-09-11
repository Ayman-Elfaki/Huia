using Finbuckle.MultiTenant.Abstractions;
using Finbuckle.MultiTenant.EntityFrameworkCore.Extensions;
using Finbuckle.MultiTenant.Identity.EntityFrameworkCore;
using Huia.EntityFrameworkCore;
using Huia.OpenId.EntityFrameworkCore.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.EntityFrameworkCore.Models;

namespace Huia.OpenId.EntityFrameworkCore;

/// <summary>
/// The Huia database context. Derives from Finbuckle's <see cref="MultiTenantIdentityDbContext{TUser,TRole,TKey}"/>
/// so every Identity entity carries a <c>TenantId</c>, is filtered by the ambient tenant on read, and is
/// rejected on write when it belongs to a different tenant (or none). The OpenIddict entities are layered
/// on top via <c>ModelBuilder.UseOpenIddict()</c> and stay tenant-agnostic — a client's tenant lives in
/// its <c>Properties["huia:tenant"]</c> entry and is enforced by the custom OpenIddict stores.
/// </summary>
/// <remarks>
/// Two deliberate departures from the defaults:
/// <list type="bullet">
/// <item>Every table is renamed to a <c>Huia*</c> name — there are no <c>AspNet*</c> or <c>OpenIddict*</c>
/// tables in the database.</item>
/// <item>The Identity global unique indexes on normalized user name / role name and the index on
/// normalized email are removed and replaced with explicit tenant-scoped composite indexes
/// (<c>{TenantId, Normalized*}</c>), because a user name or email is only unique <em>within</em> a tenant.
/// This runs after Finbuckle's own index adjustment and supersedes it deterministically.</item>
/// </list>
/// The ambient tenant is snapshotted from <see cref="IMultiTenantContextAccessor"/> when the context is
/// constructed: during a request it is the routing tenant; for seeding / background work set it first
/// with <c>HuiaTenantScope.Enter(...)</c>.
/// </remarks>
public class HuiaDbContext : MultiTenantIdentityDbContext<HuiaUser, HuiaRole, string>
{
    /// <summary>Creates the context bound to the ambient tenant.</summary>
    /// <param name="multiTenantContextAccessor">Supplies the tenant the instance is bound to.</param>
    /// <param name="options">The context options, configured by the host with a concrete provider.</param>
    public HuiaDbContext(IMultiTenantContextAccessor multiTenantContextAccessor, DbContextOptions<HuiaDbContext> options)
        : base(multiTenantContextAccessor, options)
    {
    }

    /// <summary>The per-tenant signing keys managed by the key-lifecycle jobs. Not a multi-tenant entity —
    /// the key services filter it by <c>TenantId</c> explicitly, so the jobs can run with no tenant in scope.</summary>
    public DbSet<HuiaSigningKey> SigningKeys => Set<HuiaSigningKey>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.UseOpenIddict();

        builder.ConfigureHuiaIdentitySchema<HuiaUser, HuiaRole>();
        RenameOpenIddictTables(builder);
        ApplyTenantScopedIndexes(builder);
        MakePasskeysMultiTenant(builder);
        ConfigureSigningKeys(builder);
    }

    /// <summary>
    /// Makes the passkey (WebAuthn credential) entity multi-tenant, on top of the plain configuration
    /// <see cref="HuiaIdentitySchemaConventions.ConfigureHuiaIdentitySchema{TUser,TRole}"/> already applied
    /// — when Finbuckle has not already done so (the base <c>OnModelCreating</c> only makes it multi-tenant
    /// when <c>IdentityOptions.Stores.SchemaVersion</c> resolves to <c>Version3</c> — true on the DI path,
    /// not when the context is constructed directly by the model tests or a design-time factory) — so a
    /// credential is scoped to its tenant on read and stamped on write like every other Identity row.
    /// </summary>
    private static void MakePasskeysMultiTenant(ModelBuilder builder)
    {
        var alreadyMultiTenant = builder.Model
            .FindEntityType(typeof(IdentityUserPasskey<string>))!
            .FindProperty("TenantId") is not null;

        if (!alreadyMultiTenant)
        {
            builder.Entity<IdentityUserPasskey<string>>().IsMultiTenant().AdjustUniqueIndexes();
        }

        builder.Entity<IdentityUserPasskey<string>>()
            .HasIndex(["TenantId", nameof(IdentityUserPasskey<string>.UserId)], "IX_HuiaUserPasskeys_Tenant_User");
    }

    private static void RenameOpenIddictTables(ModelBuilder builder)
    {
        builder.Entity<OpenIddictEntityFrameworkCoreApplication>().ToTable("HuiaApplications");
        builder.Entity<OpenIddictEntityFrameworkCoreAuthorization>().ToTable("HuiaAuthorizations");
        builder.Entity<OpenIddictEntityFrameworkCoreScope>().ToTable("HuiaScopes");
        builder.Entity<OpenIddictEntityFrameworkCoreToken>().ToTable("HuiaTokens");
    }

    private static void ApplyTenantScopedIndexes(ModelBuilder builder)
    {
        var user = builder.Entity<HuiaUser>().Metadata;
        foreach (var index in user.GetIndexes().ToList())
        {
            user.RemoveIndex(index);
        }

        var role = builder.Entity<HuiaRole>().Metadata;
        foreach (var index in role.GetIndexes().ToList())
        {
            role.RemoveIndex(index);
        }

        builder.Entity<HuiaUser>(b =>
        {
            b.Property(u => u.TenantId).HasMaxLength(64).IsRequired();
            b.HasIndex(u => new { u.TenantId, u.NormalizedUserName }, "IX_HuiaUsers_Tenant_UserName").IsUnique();
            b.HasIndex(u => new { u.TenantId, u.NormalizedEmail }, "IX_HuiaUsers_Tenant_Email");
        });

        builder.Entity<HuiaRole>(b =>
        {
            b.Property(r => r.TenantId).HasMaxLength(64).IsRequired();
            b.HasIndex(r => new { r.TenantId, r.NormalizedName }, "IX_HuiaRoles_Tenant_Name").IsUnique();
        });
    }

    private static void ConfigureSigningKeys(ModelBuilder builder)
    {
        builder.Entity<HuiaSigningKey>(b =>
        {
            b.ToTable("HuiaSigningKeys");
            b.HasKey(k => k.Id);
            b.Property(k => k.Id).HasMaxLength(64);
            b.Property(k => k.TenantId).HasMaxLength(64).IsRequired();
            b.Property(k => k.KeyId).HasMaxLength(128).IsRequired();
            b.Property(k => k.Algorithm).HasMaxLength(16).IsRequired();
            b.Property(k => k.PublicJwkJson).IsRequired();
            b.Property(k => k.WrappedPrivateKey).IsRequired();
            b.Property(k => k.Status).HasConversion<string>().HasMaxLength(16);
            b.HasIndex(k => new { k.TenantId, k.Status });
            b.HasIndex(k => new { k.TenantId, k.KeyId }).IsUnique();
        });
    }
}
