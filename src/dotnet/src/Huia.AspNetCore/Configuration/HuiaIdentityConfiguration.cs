using Huia.AspNetCore.Identity;
using Huia.EntityFrameworkCore;
using Huia.EntityFrameworkCore.Entities;
using Huia.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.AspNetCore.Configuration;

/// <summary>
/// Registers ASP.NET Core Identity for <see cref="HuiaUser"/> / <see cref="HuiaRole"/> on the stock EF
/// Core stores. Tenant isolation is the job of <c>HuiaDbContext</c> (it derives from Finbuckle's
/// <c>MultiTenantIdentityDbContext</c>: a global query filter on read, write-time enforcement on save).
/// The <c>AddIdentity</c> options lambda here is a fixed, non-computed baseline — it is never what a
/// real request is served from. Every resolvable tenant is served by the per-tenant projection
/// (<c>AddHuiaPerTenantIdentityOptions</c>) or a named per-flow projection (<c>AddHuiaFlowIdentity</c>);
/// password complexity, lockout, and the confirmed-email / confirmed-phone split all live there now,
/// each flow carrying its own policy instead of one shared across a tenant.
/// </summary>
internal static class HuiaIdentityConfiguration
{
    public static IServiceCollection AddHuiaIdentity(this IServiceCollection services, HuiaOptions options)
    {
        services.AddIdentity<HuiaUser, HuiaRole>(identity =>
            {
                // Cross-tenant uniqueness is a composite index in HuiaDbContext; Identity's own check is
                // opt-in per tenant, so the baseline stays off.
                identity.User.RequireUniqueEmail = false;

                // Confirmed-email / confirmed-phone gating varies per flow, so it is applied to the named
                // per-flow options by AddHuiaFlowIdentity — never here, where one tenant's rule would leak
                // onto every flow-agnostic sign-in.
                identity.SignIn.RequireConfirmedEmail = false;
                identity.SignIn.RequireConfirmedAccount = false;
                identity.SignIn.RequireConfirmedPhoneNumber = false;

                // Version3 is what maps the passkey (WebAuthn credential) entity into the model — without
                // it every UserManager/SignInManager passkey call throws. HuiaDbContext reads this back
                // through its application service provider when building the model.
                identity.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
            })
            .AddEntityFrameworkStores<HuiaDbContext>()
            .AddDefaultTokenProviders()
            .AddUserManager<HuiaUserManager>()
            .AddSignInManager<HuiaSignInManager>();

        ConfigurePasskeyServer(services, options.Passkey);

        return services;
    }

    /// <summary>
    /// Projects the host-wide relying-party settings onto <see cref="IdentityPasskeyOptions"/>. The
    /// relying-party id defaults to the request host (correct for a single-host deployment); origin
    /// validation falls back to the framework's same-origin check unless extra origins are configured.
    /// </summary>
    private static void ConfigurePasskeyServer(IServiceCollection services, HuiaPasskeyServerOptions passkey)
    {
        services.Configure<IdentityPasskeyOptions>(identity =>
        {
            if (!string.IsNullOrWhiteSpace(passkey.RelyingPartyId))
            {
                identity.ServerDomain = passkey.RelyingPartyId;
            }

            if (passkey.AllowedOrigins.Count > 0)
            {
                var allowed = passkey.AllowedOrigins
                    .Select(o => new Uri(o, UriKind.Absolute).GetLeftPart(UriPartial.Authority))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                identity.ValidateOrigin = context =>
                {
                    if (string.IsNullOrEmpty(context.Origin) || !Uri.TryCreate(context.Origin, UriKind.Absolute, out var origin))
                    {
                        return ValueTask.FromResult(false);
                    }

                    if (allowed.Contains(origin.GetLeftPart(UriPartial.Authority)))
                    {
                        return ValueTask.FromResult(true);
                    }

                    var requestOrigin = context.HttpContext.Request.Headers.Origin.ToString();
                    return ValueTask.FromResult(!context.CrossOrigin && string.Equals(requestOrigin, context.Origin, StringComparison.Ordinal));
                };
            }
        });
    }
}
