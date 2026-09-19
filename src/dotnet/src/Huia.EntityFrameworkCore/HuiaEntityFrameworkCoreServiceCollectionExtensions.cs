using Huia.DependencyInjection;
using Huia.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Wires ASP.NET Core Identity's EF Core stores onto a host's own <c>DbContext</c>.</summary>
public static class HuiaEntityFrameworkCoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers ASP.NET Core Identity's EF Core stores for <typeparamref name="TUser"/> /
    /// <typeparamref name="TRole"/> against <typeparamref name="TContext"/>, plus the schema version
    /// (<see cref="IdentitySchemaVersions.Version3"/>, required for the built-in passkey entity) and the
    /// discoverable-passkey default every Huia flavor shares. If the host has not already registered
    /// <typeparamref name="TContext"/>, this method registers its standard EF Core DI services without
    /// selecting a provider. The host must still configure a concrete provider and migrations — Huia
    /// ships neither.
    /// </summary>
    /// <typeparam name="TContext">The host's concrete <c>DbContext</c>.</typeparam>
    /// <typeparam name="TUser">The concrete user entity, at least as derived as <see cref="HuiaUser"/>.</typeparam>
    /// <typeparam name="TRole">The concrete role entity, at least as derived as <see cref="HuiaRole"/>.</typeparam>
    /// <param name="builder">The Huia builder, from <c>AddHuiaOpenId(...)</c> or <c>AddHuiaHeadless(...)</c>.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IHuiaBuilder AddEntityFrameworkCoreStores<TContext, TUser, TRole>(this IHuiaBuilder builder)
        where TContext : DbContext
        where TUser : HuiaUser, new()
        where TRole : HuiaRole, new()
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (builder.Services.All(d => d.ServiceType != typeof(DbContextOptions<TContext>)))
        {
            builder.Services.AddDbContext<TContext>();
        }

        ForwardHuiaBaseContexts(builder.Services, typeof(TContext));

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

    // Huia's internal services (key ring, admin endpoints, OpenIddict stores…) resolve Huia's own base
    // context (e.g. HuiaDbContext<HuiaUser, HuiaRole, string>). When the host registers a derived context,
    // forward each Huia base type to it so the same scoped instance serves both.
    private static void ForwardHuiaBaseContexts(IServiceCollection services, Type contextType)
    {
        for (var baseType = contextType.BaseType; baseType is not null && baseType != typeof(DbContext); baseType = baseType.BaseType)
        {
            if (baseType.Namespace?.StartsWith("Huia", StringComparison.Ordinal) != true)
            {
                continue;
            }

            services.TryAddScoped(baseType, sp => sp.GetRequiredService(contextType));
        }
    }

    /// <summary>Convenience overload for a host using the plain common <see cref="HuiaUser"/> / <see cref="HuiaRole"/> entities directly.</summary>
    /// <typeparam name="TContext">The host's concrete <c>DbContext</c>.</typeparam>
    /// <param name="builder">The Huia builder, from <c>AddHuiaOpenId(...)</c> or <c>AddHuiaHeadless(...)</c>.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IHuiaBuilder AddEntityFrameworkCoreStores<TContext>(this IHuiaBuilder builder)
        where TContext : IdentityDbContext<HuiaUser, HuiaRole, string>
        => builder.AddEntityFrameworkCoreStores<TContext, HuiaUser, HuiaRole>();

    // Note: the 1-generic-arg overload above keeps the stricter IdentityDbContext<HuiaUser,HuiaRole,string>
    // constraint deliberately — it exists specifically for a host using the plain common entities directly
    // (a single-tenant DbContext with no multi-tenancy wrapper), where that exact shape is guaranteed.
}
