using Finbuckle.MultiTenant;
using Finbuckle.MultiTenant.AspNetCore.Extensions;
using Finbuckle.MultiTenant.Extensions;
using Huia.Multitenancy;
using Huia.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Huia.Configuration;

/// <summary>
/// Wires Finbuckle with the base-path strategy and an in-memory store populated from the configured
/// tenants.
/// </summary>
internal static class HuiaMultiTenancyConfiguration
{
    public static IServiceCollection AddHuiaMultiTenancy(this IServiceCollection services, HuiaOptions options)
    {
        var tenants = options.Tenants.Keys.Select(id => new HuiaTenantInfo(id)).ToList();

        services.AddMultiTenant<HuiaTenantInfo>()
            .WithBasePathStrategy(strategy => strategy.RebaseAspNetCorePathBase = true)
            .WithInMemoryStore(store =>
            {
                store.IsCaseSensitive = true;
                foreach (var tenant in tenants)
                {
                    store.Tenants.Add(tenant);
                }
            });

        services.AddHttpContextAccessor();

        return services;
    }

    /// <summary>
    /// Projects each tenant's passkey policy onto <see cref="IdentityPasskeyOptions"/>: the relying-party
    /// id and allowed origins, the user-verification requirement, the authenticator preference and the
    /// ceremony timeout.
    /// </summary>
    public static IServiceCollection AddHuiaPerTenantPasskeyOptions(this IServiceCollection services, HuiaOptions options)
    {
        services.AddOptions<IdentityPasskeyOptions>()
            .ConfigurePerTenant<IdentityPasskeyOptions, HuiaTenantInfo>((passkey, tenant) =>
            {
                if (!options.Tenants.TryGetValue(tenant.Identifier, out var config) || config.Authentication.Passkey is not { } policy)
                {
                    return;
                }

                passkey.UserVerificationRequirement = policy.UserVerification switch
                {
                    PasskeyUserVerification.Required => "required",
                    PasskeyUserVerification.Discouraged => "discouraged",
                    _ => "preferred",
                };
                passkey.AuthenticatorAttachment = policy.AuthenticatorAttachment switch
                {
                    PasskeyAuthenticatorAttachment.Platform => "platform",
                    PasskeyAuthenticatorAttachment.CrossPlatform => "cross-platform",
                    _ => null,
                };
                passkey.AuthenticatorTimeout = policy.AuthenticatorTimeout;

                if (!string.IsNullOrWhiteSpace(policy.RelyingPartyId))
                {
                    passkey.ServerDomain = policy.RelyingPartyId;
                }

                if (policy.AllowedOrigins.Count > 0)
                {
                    var allowed = policy.AllowedOrigins
                        .Select(o => new Uri(o, UriKind.Absolute).GetLeftPart(UriPartial.Authority))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    passkey.ValidateOrigin = context =>
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

        services.AddScoped<IOptions<IdentityPasskeyOptions>>(sp =>
            new OptionsWrapper<IdentityPasskeyOptions>(sp.GetRequiredService<IOptionsSnapshot<IdentityPasskeyOptions>>().Value));

        return services;
    }

    /// <summary>
    /// Projects each tenant's password-complexity policy (and the opt-in unique-email rule) onto the
    /// default <see cref="IdentityOptions"/> for that tenant.
    /// </summary>
    public static IServiceCollection AddHuiaPerTenantIdentityOptions(this IServiceCollection services, HuiaOptions options)
    {
        services.AddOptions<IdentityOptions>()
            .ConfigurePerTenant<IdentityOptions, HuiaTenantInfo>((identity, tenant) =>
            {
                if (!options.Tenants.TryGetValue(tenant.Identifier, out var config))
                {
                    return;
                }

                var emailAndPassword = config.Authentication.EmailAndPassword;
                identity.Password.RequiredLength = emailAndPassword.MinimumLength;
                identity.Password.RequireDigit = emailAndPassword.RequireDigit;
                identity.Password.RequireLowercase = emailAndPassword.RequireLowercase;
                identity.Password.RequireUppercase = emailAndPassword.RequireUppercase;
                identity.Password.RequireNonAlphanumeric = emailAndPassword.RequireNonAlphanumeric;
                identity.Password.RequiredUniqueChars = emailAndPassword.RequiredUniqueChars;
                identity.User.RequireUniqueEmail = emailAndPassword.RequireUniqueEmail;
            });

        services.AddScoped<IOptions<IdentityOptions>>(sp =>
            new OptionsWrapper<IdentityOptions>(sp.GetRequiredService<IOptionsSnapshot<IdentityOptions>>().Value));

        return services;
    }
}
