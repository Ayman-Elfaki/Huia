using Finbuckle.MultiTenant;
using Finbuckle.MultiTenant.Abstractions;
using Finbuckle.MultiTenant.Identity.EntityFrameworkCore;
using Huia.EntityFrameworkCore.Entities;
using Huia.Identity;
using Huia.Multitenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Huia.EntityFrameworkCore;

/// <summary>
/// The common Huia base database context. Derives from Finbuckle's <see cref="MultiTenantIdentityDbContext{TUser,TRole,TKey}"/>
/// so every Identity entity carries a <c>TenantId</c>, is filtered by the ambient tenant on read, and is
/// rejected on write when it belongs to a different tenant (or none).
/// </summary>
public class HuiaDbContext : MultiTenantIdentityDbContext<HuiaUser, HuiaRole, string>
{
    private static readonly IMultiTenantContextAccessor NullAccessor = new StaticMultiTenantContextAccessor<HuiaTenantInfo>(null);

    /// <summary>Creates the context bound to the ambient tenant.</summary>
    /// <param name="multiTenantContextAccessor">Supplies the tenant the instance is bound to.</param>
    /// <param name="options">The context options, configured by the host with a concrete provider.</param>
    public HuiaDbContext(IMultiTenantContextAccessor multiTenantContextAccessor, DbContextOptions<HuiaDbContext> options)
        : base(multiTenantContextAccessor, options)
    {
    }

    /// <summary>Creates the context with options (unscoped / ambient tenant null).</summary>
    /// <param name="options">The context options.</param>
    public HuiaDbContext(DbContextOptions<HuiaDbContext> options)
        : base(NullAccessor, options)
    {
    }

    /// <summary>Creates the context bound to the ambient tenant for derived context types.</summary>
    /// <param name="multiTenantContextAccessor">Supplies the tenant the instance is bound to.</param>
    /// <param name="options">The generic context options.</param>
    protected HuiaDbContext(IMultiTenantContextAccessor multiTenantContextAccessor, DbContextOptions options)
        : base(multiTenantContextAccessor, options)
    {
    }

    /// <summary>Creates the context with generic options for derived context types.</summary>
    /// <param name="options">The generic context options.</param>
    protected HuiaDbContext(DbContextOptions options)
        : base(NullAccessor, options)
    {
    }

    /// <summary>The per-tenant signing keys managed by the key-lifecycle jobs. Not a multi-tenant entity —
    /// the key services filter it by <c>TenantId</c> explicitly, so the jobs can run with no tenant in scope.</summary>
    public DbSet<HuiaSigningKey> SigningKeys => Set<HuiaSigningKey>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        RenameIdentityTables(builder);
        ApplyTenantScopedIndexes(builder);
        ConfigureSigningKeys(builder);
    }

    /// <summary>Renames the ASP.NET Core Identity tables to <c>Huia*</c>.</summary>
    protected static void RenameIdentityTables(ModelBuilder builder)
    {
        builder.Entity<HuiaUser>().ToTable("HuiaUsers");
        builder.Entity<HuiaRole>().ToTable("HuiaRoles");
        builder.Entity<IdentityUserRole<string>>().ToTable("HuiaUserRoles");
        builder.Entity<IdentityUserClaim<string>>().ToTable("HuiaUserClaims");
        builder.Entity<IdentityUserLogin<string>>().ToTable("HuiaUserLogins");
        builder.Entity<IdentityUserToken<string>>().ToTable("HuiaUserTokens");
        builder.Entity<IdentityRoleClaim<string>>().ToTable("HuiaRoleClaims");
    }

    /// <summary>Replaces global uniqueness indexes with tenant-scoped composite indexes.</summary>
    protected static void ApplyTenantScopedIndexes(ModelBuilder builder)
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
            b.Property(u => u.FirstName).HasMaxLength(256);
            b.Property(u => u.LastName).HasMaxLength(256);
            b.HasIndex(u => new { u.TenantId, u.NormalizedUserName }, "IX_HuiaUsers_Tenant_UserName").IsUnique();
            b.HasIndex(u => new { u.TenantId, u.NormalizedEmail }, "IX_HuiaUsers_Tenant_Email");
        });

        builder.Entity<HuiaRole>(b =>
        {
            b.Property(r => r.TenantId).HasMaxLength(64).IsRequired();
            b.Property(r => r.Origin).HasMaxLength(16).IsRequired();
            b.HasIndex(r => new { r.TenantId, r.NormalizedName }, "IX_HuiaRoles_Tenant_Name").IsUnique();
        });
    }

    /// <summary>Configures the <c>HuiaSigningKeys</c> table.</summary>
    protected static void ConfigureSigningKeys(ModelBuilder builder)
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
