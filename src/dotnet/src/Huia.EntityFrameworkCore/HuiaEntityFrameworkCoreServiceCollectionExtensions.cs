using Huia.DependencyInjection;
using Huia.EntityFrameworkCore.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Wires ASP.NET Core Identity's EF Core stores onto a host's own <c>DbContext</c>.</summary>
public static class HuiaEntityFrameworkCoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers ASP.NET Core Identity's EF Core stores for <typeparamref name="TUser"/> /
    /// <typeparamref name="TRole"/> against <typeparamref name="TContext"/>, plus the schema version
    /// (<see cref="IdentitySchemaVersions.Version3"/>, required for the built-in passkey entity) and the
    /// discoverable-passkey default every Huia flavor shares. The host must register
    /// <typeparamref name="TContext"/> with a concrete provider before calling this — Huia ships no
    /// Entity Framework Core provider and no migrations.
    /// </summary>
    /// <typeparam name="TContext">The host's concrete <c>DbContext</c>.</typeparam>
    /// <typeparam name="TUser">The concrete user entity, at least as derived as <see cref="HuiaUser"/>.</typeparam>
    /// <typeparam name="TRole">The concrete role entity, at least as derived as <see cref="HuiaRole"/>.</typeparam>
    /// <param name="builder">The Huia builder, from <c>AddHuia()</c>.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <exception cref="InvalidOperationException"><typeparamref name="TContext"/> was not registered first.</exception>
    public static IHuiaBuilder AddEntityFrameworkCoreStores<TContext, TUser, TRole>(this IHuiaBuilder builder)
        where TContext : DbContext
        where TUser : HuiaUser, new()
        where TRole : HuiaRole, new()
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (builder.Services.All(d => d.ServiceType != typeof(DbContextOptions<TContext>)))
        {
            throw new InvalidOperationException(
                $"{typeof(TContext).Name} must be registered before AddEntityFrameworkCoreStores is called, " +
                $"for example services.AddDbContext<{typeof(TContext).Name}>(o => o.UseNpgsql(connectionString)). " +
                "Huia ships no Entity Framework Core provider.");
        }

        // AddIdentityCore + AddRoles (not the full AddIdentity) — that's the base UserManager/RoleManager
        // registration without pulling in a specific authentication scheme, since that choice (cookies for
        // OpenId, bearer tokens for Headless) belongs to the flavor-specific extension that runs next.
        builder.Services
            .AddIdentityCore<TUser>(o =>
            {
                // Version3 is what maps the passkey (WebAuthn credential) entity into the model — without
                // it every UserManager/SignInManager passkey call throws.
                o.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
            })
            .AddRoles<TRole>()
            .AddEntityFrameworkStores<TContext>()
            .AddDefaultTokenProviders();

        // Every credential Huia issues is discoverable, so a usernameless assertion can find it.
        builder.Services.Configure<IdentityPasskeyOptions>(o => o.ResidentKeyRequirement = "required");

        return builder;
    }

    /// <summary>Convenience overload for a host using the plain common <see cref="HuiaUser"/> / <see cref="HuiaRole"/> entities directly.</summary>
    /// <typeparam name="TContext">The host's concrete <c>DbContext</c>.</typeparam>
    /// <param name="builder">The Huia builder, from <c>AddHuia()</c>.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IHuiaBuilder AddEntityFrameworkCoreStores<TContext>(this IHuiaBuilder builder)
        where TContext : IdentityDbContext<HuiaUser, HuiaRole, string>
        => builder.AddEntityFrameworkCoreStores<TContext, HuiaUser, HuiaRole>();

    // Note: the 1-generic-arg overload above keeps the stricter IdentityDbContext<HuiaUser,HuiaRole,string>
    // constraint deliberately — it exists specifically for a host using the plain common entities directly
    // (a single-tenant DbContext with no multi-tenancy wrapper), where that exact shape is guaranteed.
}
