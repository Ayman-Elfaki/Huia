using Huia.AspNetCore.Identity;
using Huia.EntityFrameworkCore;
using Huia.EntityFrameworkCore.Entities;
using Huia.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Huia.AspNetCore.Configuration;

/// <summary>
/// Registers ASP.NET Core Identity for <see cref="HuiaUser"/> / <see cref="HuiaRole"/> on the stock EF
/// Core stores. Tenant isolation is the job of <c>HuiaDbContext</c> (it derives from Finbuckle's
/// <c>MultiTenantIdentityDbContext</c>: a global query filter on read, write-time enforcement on save).
/// The <c>AddIdentity</c> options lambda here is the process-global fallback: every value that varies
/// per tenant is set to the least restrictive value across the configured tenants. The per-tenant
/// values are projected onto <c>IdentityOptions</c> by <c>AddHuiaPerTenantIdentityOptions</c>; the
/// custom <c>IUserConfirmation</c> still handles the confirmed-email vs confirmed-phone split per flow.
/// </summary>
internal static class HuiaIdentityConfiguration
{
    public static IServiceCollection AddHuiaIdentity(this IServiceCollection services, HuiaOptions options)
    {
        var tenants = options.Tenants.Values.ToList();
        var password = tenants.Select(t => t.Authentication.Password).ToList();
        var lockout = tenants.Select(t => t.Lockout).ToList();

        services.AddIdentity<HuiaUser, HuiaRole>(identity =>
            {
                // Cross-tenant uniqueness is a composite index in HuiaDbContext; Identity's own check is
                // opt-in per tenant, so the global fallback stays off.
                identity.User.RequireUniqueEmail = false;

                // Least restrictive across tenants: shortest minimum, a complexity rule on only if every
                // tenant wants it, fewest distinct characters.
                identity.Password.RequiredLength = password.Count == 0 ? 10 : password.Min(p => p.MinimumLength);
                identity.Password.RequireDigit = password.Count != 0 && password.All(p => p.RequireDigit);
                identity.Password.RequireLowercase = password.Count != 0 && password.All(p => p.RequireLowercase);
                identity.Password.RequireUppercase = password.Count != 0 && password.All(p => p.RequireUppercase);
                identity.Password.RequireNonAlphanumeric = password.Count != 0 && password.All(p => p.RequireNonAlphanumeric);
                identity.Password.RequiredUniqueChars = password.Count == 0 ? 1 : password.Min(p => p.RequiredUniqueChars);

                // Least restrictive: most attempts allowed, shortest lockout.
                identity.Lockout.MaxFailedAccessAttempts = lockout.Count == 0 ? 5 : lockout.Max(l => l.MaxFailedAccessAttempts);
                identity.Lockout.DefaultLockoutTimeSpan = lockout.Count == 0 ? TimeSpan.FromMinutes(15) : lockout.Min(l => l.LockoutDuration);
                identity.Lockout.AllowedForNewUsers = lockout.Count != 0 && lockout.All(l => l.AllowedForNewUsers);

                // Union across tenants with the password flow on; the custom IUserConfirmation narrows
                // this per flow.
                var anyTenantRequiresConfirmedEmail = password.Any(p => p is { Enabled: true, RequireConfirmedEmail: true });
                identity.SignIn.RequireConfirmedEmail = anyTenantRequiresConfirmedEmail;
                identity.SignIn.RequireConfirmedAccount = anyTenantRequiresConfirmedEmail;
                identity.SignIn.RequireConfirmedPhoneNumber = false;
            })
            .AddEntityFrameworkStores<HuiaDbContext>()
            .AddDefaultTokenProviders();

        services.Replace(ServiceDescriptor.Scoped<IUserConfirmation<HuiaUser>, HuiaUserConfirmation>());

        return services;
    }
}
