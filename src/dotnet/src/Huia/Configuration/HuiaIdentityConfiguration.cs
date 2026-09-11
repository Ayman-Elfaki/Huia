using Huia.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.Configuration;

/// <summary>
/// Registers ASP.NET Core Identity for <see cref="HuiaUser"/> / <see cref="HuiaRole"/>.
/// The <c>AddIdentity</c> options lambda here is a fixed, non-computed baseline — it is never what a
/// real request is served from. Every resolvable tenant is served by the per-tenant projection
/// (<c>AddHuiaPerTenantIdentityOptions</c>) or a named per-flow projection (<c>AddHuiaFlowIdentity</c>).
/// </summary>
internal static class HuiaIdentityConfiguration
{
    public static IServiceCollection AddHuiaIdentity(this IServiceCollection services)
    {
        services.AddIdentity<HuiaUser, HuiaRole>(identity =>
            {
                // Cross-tenant uniqueness is a composite index; Identity's own check is opt-in per tenant, so the baseline stays off.
                identity.User.RequireUniqueEmail = false;

                // Confirmed-email / confirmed-phone gating varies per flow, so it is applied to the named
                // per-flow options by AddHuiaFlowIdentity — never here, where one tenant's rule would leak
                // onto every flow-agnostic sign-in.
                identity.SignIn.RequireConfirmedEmail = false;
                identity.SignIn.RequireConfirmedAccount = false;
                identity.SignIn.RequireConfirmedPhoneNumber = false;

                // Version3 is what maps the passkey (WebAuthn credential) entity into the model — without
                // it every UserManager/SignInManager passkey call throws.
                identity.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
            })
            .AddDefaultTokenProviders()
            .AddUserManager<HuiaUserManager>()
            .AddSignInManager<HuiaSignInManager>();

        // The one globally-fixed passkey setting: every credential Huia issues is discoverable so a
        // usernameless assertion can find it. The relying-party id / origins / user-verification policy
        // are per-tenant and projected onto IdentityPasskeyOptions by AddHuiaPerTenantPasskeyOptions.
        services.Configure<IdentityPasskeyOptions>(identity => identity.ResidentKeyRequirement = "required");

        return services;
    }
}
