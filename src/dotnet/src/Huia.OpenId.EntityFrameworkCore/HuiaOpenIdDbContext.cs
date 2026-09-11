using Finbuckle.MultiTenant.Abstractions;
using Finbuckle.MultiTenant.EntityFrameworkCore.Extensions;
using Huia.EntityFrameworkCore.Entities;
using Huia.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.EntityFrameworkCore.Models;

namespace Huia.OpenId.EntityFrameworkCore;

/// <summary>
/// The Huia OpenID database context. Derives from <see cref="Huia.EntityFrameworkCore.HuiaDbContext"/>
/// and maps OpenIddict tables (<c>HuiaApplications</c>, <c>HuiaAuthorizations</c>, <c>HuiaScopes</c>,
/// <c>HuiaTokens</c>) and passkeys (<c>HuiaUserPasskeys</c>).
/// </summary>
public class HuiaOpenIdDbContext : Huia.EntityFrameworkCore.HuiaDbContext
{
    /// <summary>Creates the context with options.</summary>
    /// <param name="options">The options for this context.</param>
    public HuiaOpenIdDbContext(DbContextOptions<HuiaOpenIdDbContext> options)
        : base(options)
    {
    }

    /// <summary>Creates the context with options and the multi-tenant accessor.</summary>
    /// <param name="tenantAccessor">Supplies the ambient tenant.</param>
    /// <param name="options">The options for this context.</param>
    public HuiaOpenIdDbContext(IMultiTenantContextAccessor tenantAccessor, DbContextOptions<HuiaOpenIdDbContext> options)
        : base(tenantAccessor, options)
    {
    }

    /// <summary>Protected constructor for subclasses.</summary>
    /// <param name="options">The generic DbContext options.</param>
    protected HuiaOpenIdDbContext(DbContextOptions options)
        : base(options)
    {
    }

    /// <summary>Protected constructor for subclasses with multi-tenant accessor.</summary>
    /// <param name="tenantAccessor">Supplies the ambient tenant.</param>
    /// <param name="options">The generic DbContext options.</param>
    protected HuiaOpenIdDbContext(IMultiTenantContextAccessor tenantAccessor, DbContextOptions options)
        : base(tenantAccessor, options)
    {
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.UseOpenIddict();

        RenameOpenIddictTables(builder);
        ConfigurePasskeys(builder);
    }

    private static void RenameOpenIddictTables(ModelBuilder builder)
    {
        builder.Entity<OpenIddictEntityFrameworkCoreApplication>().ToTable("HuiaApplications");
        builder.Entity<OpenIddictEntityFrameworkCoreAuthorization>().ToTable("HuiaAuthorizations");
        builder.Entity<OpenIddictEntityFrameworkCoreScope>().ToTable("HuiaScopes");
        builder.Entity<OpenIddictEntityFrameworkCoreToken>().ToTable("HuiaTokens");
    }

    private static void ConfigurePasskeys(ModelBuilder builder)
    {
        var passkey = builder.Entity<IdentityUserPasskey<string>>();
        passkey.ToTable("HuiaUserPasskeys");
        passkey.HasKey(p => p.CredentialId);
        passkey.Property(p => p.CredentialId).HasMaxLength(1024);
        passkey.OwnsOne(p => p.Data, owned => owned.ToJson());

        builder.Entity<HuiaUser>()
            .HasMany<IdentityUserPasskey<string>>()
            .WithOne()
            .HasForeignKey(p => p.UserId)
            .IsRequired();

        var alreadyMultiTenant = builder.Model
            .FindEntityType(typeof(IdentityUserPasskey<string>))!
            .FindProperty("TenantId") is not null;

        if (!alreadyMultiTenant)
        {
            passkey.IsMultiTenant().AdjustUniqueIndexes();
        }

        builder.Entity<IdentityUserPasskey<string>>()
            .HasIndex(["TenantId", nameof(IdentityUserPasskey<string>.UserId)], "IX_HuiaUserPasskeys_Tenant_User");
    }
}
