using Finbuckle.MultiTenant.Abstractions;
using Huia.EntityFrameworkCore;
using Huia.Headless.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Huia.Headless.EntityFrameworkCore;

/// <summary>
/// Database context for Huia Headless applications.
/// Derives from <see cref="HuiaDbContext"/> and maps the <see cref="HuiaRefreshToken"/> table.
/// </summary>
public class HuiaHeadlessDbContext : HuiaDbContext
{
    /// <summary>Creates the context with options.</summary>
    /// <param name="options">The options for this context.</param>
    public HuiaHeadlessDbContext(DbContextOptions<HuiaHeadlessDbContext> options)
        : base(options)
    {
    }

    /// <summary>Creates the context with options and the multi-tenant accessor.</summary>
    /// <param name="tenantAccessor">Supplies the ambient tenant.</param>
    /// <param name="options">The options for this context.</param>
    public HuiaHeadlessDbContext(IMultiTenantContextAccessor tenantAccessor, DbContextOptions<HuiaHeadlessDbContext> options)
        : base(tenantAccessor, options)
    {
    }

    /// <summary>Protected constructor for subclasses.</summary>
    /// <param name="options">The generic DbContext options.</param>
    protected HuiaHeadlessDbContext(DbContextOptions options)
        : base(options)
    {
    }

    /// <summary>Protected constructor for subclasses with multi-tenant accessor.</summary>
    /// <param name="tenantAccessor">Supplies the ambient tenant.</param>
    /// <param name="options">The generic DbContext options.</param>
    protected HuiaHeadlessDbContext(IMultiTenantContextAccessor tenantAccessor, DbContextOptions options)
        : base(tenantAccessor, options)
    {
    }

    /// <summary>Persisted refresh tokens for headless authentication sessions.</summary>
    public DbSet<HuiaRefreshToken> RefreshTokens => Set<HuiaRefreshToken>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<HuiaRefreshToken>(b =>
        {
            b.ToTable("HuiaRefreshTokens");
            b.HasKey(t => t.Id);
            b.Property(t => t.TenantId).IsRequired().HasMaxLength(64);
            b.Property(t => t.UserId).IsRequired().HasMaxLength(128);
            b.Property(t => t.TokenHash).IsRequired().HasMaxLength(128);
            b.Property(t => t.FamilyId).IsRequired().HasMaxLength(64);
            b.Property(t => t.ReplacedByTokenHash).HasMaxLength(128);

            b.HasIndex(t => new { t.TenantId, t.TokenHash }).IsUnique();
            b.HasIndex(t => new { t.TenantId, t.FamilyId });
            b.HasIndex(t => new { t.TenantId, t.UserId });
        });
    }
}
