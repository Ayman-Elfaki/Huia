using Finbuckle.MultiTenant.Abstractions;
using Finbuckle.MultiTenant.EntityFrameworkCore.Extensions;
using Finbuckle.MultiTenant.Identity.EntityFrameworkCore;
using Huia.OpenId.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using OpenIddict.EntityFrameworkCore.Models;

namespace Huia.OpenId.EntityFrameworkCore;

/// <summary>
/// The Huia database context. Derives from Finbuckle's <see cref="MultiTenantIdentityDbContext{TUser,TRole,TKey}"/>
/// so every Identity entity carries a <c>TenantId</c>, is filtered by the ambient tenant on read, and is
/// rejected on write when it belongs to a different tenant (or none). The OpenIddict entities are layered
/// on top via <c>ModelBuilder.UseOpenIddict()</c>. Applications and scopes get a real <c>TenantId</c> column
/// (a shadow property) with a unique <c>{TenantId, ClientId}</c> / <c>{TenantId, Name}</c> index, filtered by
/// the custom OpenIddict stores; authorizations and tokens are scoped through their application.
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
/// <typeparam name="TUser">The tenant-aware Huia user entity.</typeparam>
/// <typeparam name="TRole">The tenant-aware Huia role entity.</typeparam>
/// <typeparam name="TId">The Identity key marker; Huia's built-in entities use <see cref="string"/> keys.</typeparam>
public class HuiaDbContext<TUser, TRole, TId> : MultiTenantIdentityDbContext<TUser, TRole, string>
    where TUser : HuiaUser
    where TRole : HuiaRole
    where TId : notnull
{
    /// <summary>Creates the context bound to the ambient tenant.</summary>
    /// <param name="multiTenantContextAccessor">Supplies the tenant the instance is bound to.</param>
    /// <param name="options">The context options, configured by the host with a concrete provider.</param>
    public HuiaDbContext(IMultiTenantContextAccessor multiTenantContextAccessor, DbContextOptions options)
        : base(multiTenantContextAccessor, options)
    {
    }

    /// <summary>The per-tenant signing keys managed by the key-lifecycle jobs. Not a multi-tenant entity —
    /// the key services filter it by <c>TenantId</c> explicitly, so the jobs can run with no tenant in scope.</summary>
    public DbSet<HuiaSigningKey> SigningKeys => Set<HuiaSigningKey>();

    /// <summary>Persisted, runtime-defined tenants.</summary>
    public DbSet<HuiaTenantRecord> TenantRecords => Set<HuiaTenantRecord>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Invoke IdentityDbContext.OnModelCreating directly to configure Identity tables without
        // triggering Finbuckle 10's AdjustUniqueIndexes(), which has a binary breaking change on EF Core 11
        // (Huia's ApplyTenantScopedIndexes deterministically replaces Identity unique indexes anyway).
        InvokeIdentityOnModelCreating(this, builder);

        builder.Entity<TUser>().IsMultiTenant();
        builder.Entity<TRole>().IsMultiTenant();
        builder.Entity<IdentityUserRole<string>>().IsMultiTenant();
        builder.Entity<IdentityUserClaim<string>>().IsMultiTenant();
        builder.Entity<IdentityUserLogin<string>>().IsMultiTenant();
        builder.Entity<IdentityUserToken<string>>().IsMultiTenant();
        builder.Entity<IdentityRoleClaim<string>>().IsMultiTenant();
        builder.Entity<IdentityUserPasskey<string>>().IsMultiTenant();

        builder.ConfigureMultiTenant();

        builder.UseOpenIddict();

        builder.ConfigureHuiaIdentitySchema<TUser, TRole>();
        RenameOpenIddictTables(builder);
        ApplyTenantScopedIndexes(builder);
        MakePasskeysMultiTenant(builder);
        ConfigureSigningKeys(builder);
        ConfigureTenantRecords(builder);
    }

    private static readonly Action<object, ModelBuilder> InvokeIdentityOnModelCreating = CreateIdentityOnModelCreatingInvoker();

    private static Action<object, ModelBuilder> CreateIdentityOnModelCreatingInvoker()
    {
        var type = typeof(HuiaDbContext<TUser, TRole, TId>).BaseType;
        while (type != null && !type.Name.StartsWith("IdentityDbContext", StringComparison.Ordinal))
        {
            type = type.BaseType;
        }

        var identityContextType = type ?? throw new InvalidOperationException("IdentityDbContext base type not found.");
        var onModelCreatingMethod = identityContextType.GetMethod(
            "OnModelCreating",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
            [typeof(ModelBuilder)])!;

        var dm = new System.Reflection.Emit.DynamicMethod(
            "InvokeBaseIdentityOnModelCreating",
            typeof(void),
            [typeof(object), typeof(ModelBuilder)],
            typeof(HuiaDbContext<TUser, TRole, TId>).Module,
            skipVisibility: true);

        var il = dm.GetILGenerator();
        il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
        il.Emit(System.Reflection.Emit.OpCodes.Castclass, identityContextType);
        il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1);
        il.Emit(System.Reflection.Emit.OpCodes.Call, onModelCreatingMethod);
        il.Emit(System.Reflection.Emit.OpCodes.Ret);

        return (Action<object, ModelBuilder>)dm.CreateDelegate(typeof(Action<object, ModelBuilder>));
    }

    /// <summary>
    /// Makes the passkey (WebAuthn credential) entity multi-tenant, on top of the plain configuration
    /// <see cref="HuiaIdentitySchemaConventions.ConfigureHuiaIdentitySchema{TUser,TRole}"/> already applied
    /// — so a credential is scoped to its tenant on read and stamped on write like every other Identity row.
    /// </summary>
    private static void MakePasskeysMultiTenant(ModelBuilder builder)
    {
        var entity = builder.Model.FindEntityType(typeof(IdentityUserPasskey<string>));
        if (entity is null)
        {
            return;
        }

        var alreadyMultiTenant = entity.FindProperty("TenantId") is not null;
        if (!alreadyMultiTenant)
        {
            builder.Entity<IdentityUserPasskey<string>>().IsMultiTenant();
        }

        builder.Entity<IdentityUserPasskey<string>>()
            .HasIndex(["TenantId", nameof(IdentityUserPasskey<string>.UserId)], "IX_HuiaUserPasskeys_Tenant_User");
    }

    private static void RenameOpenIddictTables(ModelBuilder builder)
    {
        builder.Entity<OpenIddictEntityFrameworkCoreApplication>(b =>
        {
            b.ToTable("HuiaApplications");
            ScopeUniqueIndexToTenant(b.Metadata, nameof(OpenIddictEntityFrameworkCoreApplication.ClientId));
            b.Property<string>("TenantId").HasMaxLength(64).IsRequired().HasDefaultValue(string.Empty);
            b.HasIndex("TenantId", nameof(OpenIddictEntityFrameworkCoreApplication.ClientId))
                .IsUnique().HasDatabaseName("IX_HuiaApplications_Tenant_ClientId");
        });
        builder.Entity<OpenIddictEntityFrameworkCoreAuthorization>().ToTable("HuiaAuthorizations");
        builder.Entity<OpenIddictEntityFrameworkCoreScope>(b =>
        {
            b.ToTable("HuiaScopes");
            ScopeUniqueIndexToTenant(b.Metadata, nameof(OpenIddictEntityFrameworkCoreScope.Name));
            b.Property<string>("TenantId").HasMaxLength(64).IsRequired().HasDefaultValue(string.Empty);
            b.HasIndex("TenantId", nameof(OpenIddictEntityFrameworkCoreScope.Name))
                .IsUnique().HasDatabaseName("IX_HuiaScopes_Tenant_Name");
        });
        builder.Entity<OpenIddictEntityFrameworkCoreToken>().ToTable("HuiaTokens");
    }

    // OpenIddict declares a globally unique index on ClientId / Name; with a tenant column the same value may
    // legitimately exist once per tenant, so the single-column index is replaced by a {TenantId, value} one.
    private static void ScopeUniqueIndexToTenant(
        Microsoft.EntityFrameworkCore.Metadata.IMutableEntityType entity, string propertyName)
    {
        foreach (var index in entity.GetIndexes()
                     .Where(i => i.IsUnique && i.Properties.Count == 1 && i.Properties[0].Name == propertyName)
                     .ToList())
        {
            entity.RemoveIndex(index);
        }
    }

    private static void ApplyTenantScopedIndexes(ModelBuilder builder)
    {
        var user = builder.Entity<TUser>().Metadata;
        foreach (var index in user.GetIndexes().ToList())
        {
            user.RemoveIndex(index);
        }

        var role = builder.Entity<TRole>().Metadata;
        foreach (var index in role.GetIndexes().ToList())
        {
            role.RemoveIndex(index);
        }

        builder.Entity<TUser>(b =>
        {
            b.Property(u => u.TenantId).HasMaxLength(64).IsRequired();
            b.HasIndex(u => new { u.TenantId, u.NormalizedUserName }, "IX_HuiaUsers_Tenant_UserName").IsUnique();
            b.HasIndex(u => new { u.TenantId, u.NormalizedEmail }, "IX_HuiaUsers_Tenant_Email");
        });

        builder.Entity<TRole>(b =>
        {
            b.Property(r => r.TenantId).HasMaxLength(64).IsRequired();
            b.HasIndex(r => new { r.TenantId, r.NormalizedName }, "IX_HuiaRoles_Tenant_Name").IsUnique();
        });
    }

    private static void ConfigureTenantRecords(ModelBuilder builder)
    {
        builder.Entity<HuiaTenantRecord>(b =>
        {
            b.ToTable("HuiaTenants");
            b.HasKey(t => t.TenantId);
            b.Property(t => t.TenantId).HasMaxLength(64);
            b.Property(t => t.Payload).IsRequired();
            b.Property(t => t.Version).IsConcurrencyToken();
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
