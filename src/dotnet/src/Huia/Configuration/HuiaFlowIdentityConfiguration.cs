using Finbuckle.MultiTenant.Extensions;
using Huia.Identity;
using Huia.Multitenancy;
using Huia.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Huia.Configuration;

/// <summary>
/// Registers one named <see cref="IdentityOptions"/> instance per authentication flow
/// (<see cref="HuiaFlowIdentityOptions"/>) and the <see cref="IHuiaFlowIdentityFactory"/> that hands a
/// flow its manager pair. Each flow's <see cref="IdentityOptions"/> is projected from that flow's own
/// options object (<see cref="EmailAndPasswordLoginOptions"/> / <see cref="PhoneOptions"/> each carry
/// their own password-or-none, lockout, and confirmation policy — there is no longer a single policy
/// shared across flows). Must run after <c>AddHuiaIdentity</c>.
/// </summary>
internal static class HuiaFlowIdentityConfiguration
{
    public static IServiceCollection AddHuiaFlowIdentity(this IServiceCollection services, HuiaOptions options)
    {
        services.AddOptions<IdentityOptions>(HuiaFlowIdentityOptions.EmailAndPassword)
            .Configure(ApplyDefaultTokenProviders)
            .ConfigurePerTenant<IdentityOptions, HuiaTenantInfo>((identity, tenant) =>
            {
                if (options.Tenants.TryGetValue(tenant.Identifier, out var config))
                {
                    ApplyEmailAndPasswordFlow(identity, config.Authentication.EmailAndPassword);
                }
            });

        services.AddOptions<IdentityOptions>(HuiaFlowIdentityOptions.PhoneLogin)
            .Configure(ApplyDefaultTokenProviders)
            .ConfigurePerTenant<IdentityOptions, HuiaTenantInfo>((identity, tenant) =>
            {
                if (options.Tenants.TryGetValue(tenant.Identifier, out var config) && config.Authentication.Phone is { } phone)
                {
                    ApplyPhoneFlow(identity, phone);
                }
            });

        services.AddOptions<IdentityOptions>(HuiaFlowIdentityOptions.ExternalLogin)
            .Configure(ApplyDefaultTokenProviders)
            .ConfigurePerTenant<IdentityOptions, HuiaTenantInfo>((identity, _) => ApplyExternalFlow(identity));

        services.AddOptions<IdentityOptions>(HuiaFlowIdentityOptions.Passkey)
            .Configure(ApplyDefaultTokenProviders)
            .ConfigurePerTenant<IdentityOptions, HuiaTenantInfo>((identity, _) => ApplyPasskeyFlow(identity));

        services.TryAddScoped<HuiaFlowIdentityFactory>();
        services.TryAddScoped<IHuiaFlowIdentityFactory>(sp => sp.GetRequiredService<HuiaFlowIdentityFactory>());

        return services;
    }

    /// <summary>
    /// Mirrors <c>IdentityBuilder.AddDefaultTokenProviders</c> onto a named <see cref="IdentityOptions"/>
    /// instance — those built-in <c>Configure</c> calls only target the default (empty) option name, so a
    /// named instance would otherwise have an empty <see cref="TokenOptions.ProviderMap"/> and every
    /// <c>Generate*TokenAsync</c> would throw. The provider types themselves are already registered by
    /// <c>AddDefaultTokenProviders</c> in <c>AddHuiaIdentity</c>.
    /// </summary>
    public static void ApplyDefaultTokenProviders(IdentityOptions identity)
    {
        identity.Tokens.ProviderMap[TokenOptions.DefaultProvider] =
            new TokenProviderDescriptor(typeof(DataProtectorTokenProvider<HuiaUser>));
        identity.Tokens.ProviderMap[TokenOptions.DefaultEmailProvider] =
            new TokenProviderDescriptor(typeof(EmailTokenProvider<HuiaUser>));
        identity.Tokens.ProviderMap[TokenOptions.DefaultPhoneProvider] =
            new TokenProviderDescriptor(typeof(PhoneNumberTokenProvider<HuiaUser>));
        identity.Tokens.ProviderMap[TokenOptions.DefaultAuthenticatorProvider] =
            new TokenProviderDescriptor(typeof(AuthenticatorTokenProvider<HuiaUser>));
    }

    /// <summary>Projects the tenant's email/password policy — including its own lockout — onto <see cref="IdentityOptions"/>.</summary>
    private static void ApplyEmailAndPasswordFlow(IdentityOptions identity, EmailAndPasswordLoginOptions config)
    {
        identity.Password.RequiredLength = config.MinimumLength;
        identity.Password.RequireDigit = config.RequireDigit;
        identity.Password.RequireLowercase = config.RequireLowercase;
        identity.Password.RequireUppercase = config.RequireUppercase;
        identity.Password.RequireNonAlphanumeric = config.RequireNonAlphanumeric;
        identity.Password.RequiredUniqueChars = config.RequiredUniqueChars;

        identity.Lockout.MaxFailedAccessAttempts = config.MaxFailedAccessAttempts;
        identity.Lockout.DefaultLockoutTimeSpan = config.LockoutDuration;
        identity.Lockout.AllowedForNewUsers = config.AllowedForNewUsers;

        identity.User.RequireUniqueEmail = config.RequireUniqueEmail;

        identity.SignIn.RequireConfirmedAccount = config.RequireConfirmedEmail;
        identity.SignIn.RequireConfirmedEmail = false;
        identity.SignIn.RequireConfirmedPhoneNumber = false;
    }

    /// <summary>Projects the tenant's phone policy — including its own lockout — onto <see cref="IdentityOptions"/>.</summary>
    private static void ApplyPhoneFlow(IdentityOptions identity, PhoneOptions config)
    {
        identity.Lockout.MaxFailedAccessAttempts = config.MaxFailedAccessAttempts;
        identity.Lockout.DefaultLockoutTimeSpan = config.LockoutDuration;
        identity.Lockout.AllowedForNewUsers = config.AllowedForNewUsers;

        identity.SignIn.RequireConfirmedPhoneNumber = config.RequireConfirmedPhoneNumber;
        identity.SignIn.RequireConfirmedAccount = false;
        identity.SignIn.RequireConfirmedEmail = false;
        identity.User.RequireUniqueEmail = false;
    }

    /// <summary>
    /// External sign-in has no tenant-configurable policy of its own — the upstream provider is the
    /// confirmed factor and there is no failed-attempt surface to lock out on.
    /// </summary>
    private static void ApplyExternalFlow(IdentityOptions identity)
    {
        identity.SignIn.RequireConfirmedAccount = false;
        identity.SignIn.RequireConfirmedEmail = false;
        identity.SignIn.RequireConfirmedPhoneNumber = false;
        identity.User.RequireUniqueEmail = false;
    }

    /// <summary>
    /// Passkey sign-in gates on nothing extra: possession of the credential (registered while
    /// authenticated) is the confirmed factor, and a WebAuthn assertion has no password-style
    /// failed-attempt surface to lock out on.
    /// </summary>
    private static void ApplyPasskeyFlow(IdentityOptions identity)
    {
        identity.SignIn.RequireConfirmedAccount = false;
        identity.SignIn.RequireConfirmedEmail = false;
        identity.SignIn.RequireConfirmedPhoneNumber = false;
        identity.User.RequireUniqueEmail = false;
    }
}
