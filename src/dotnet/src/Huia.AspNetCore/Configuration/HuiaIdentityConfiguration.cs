using Huia.AspNetCore.Identity;
using Huia.EntityFrameworkCore;
using Huia.EntityFrameworkCore.Entities;
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
    public static IServiceCollection AddHuiaIdentity(this IServiceCollection services)
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
            })
            .AddEntityFrameworkStores<HuiaDbContext>()
            .AddDefaultTokenProviders()
            .AddUserManager<HuiaUserManager>()
            .AddSignInManager<HuiaSignInManager>();

        return services;
    }
}
