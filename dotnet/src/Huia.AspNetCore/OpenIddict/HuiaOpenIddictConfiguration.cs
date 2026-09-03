using Huia.AspNetCore.OpenIddict.Handlers;
using Huia.EntityFrameworkCore;
using Huia.EntityFrameworkCore.Stores;
using Huia.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenIddict.Abstractions;
using OpenIddict.Client;
using OpenIddict.EntityFrameworkCore.Models;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Huia.AspNetCore.OpenIddict;

/// <summary>
/// Registers the OpenIddict server, validation and (when any tenant uses external login) client
/// features. The server is given a throw-away ephemeral key pair purely to satisfy OpenIddict's
/// start-up guards; the keys that actually sign and validate tokens are the per-tenant rotated keys,
/// injected by the custom handlers in <see cref="Handlers"/>.
/// </summary>
internal static class HuiaOpenIddictConfiguration
{
    public static IServiceCollection AddHuiaOpenIddict(this IServiceCollection services, HuiaOptions options)
    {
        var relaxTransport = options.DisableTransportSecurityRequirement;
        var anyExternalLogin = options.Tenants.Values.Any(t => t.Authentication.Passwordless.IsExternalLoginEnabled);

        var builder = services.AddOpenIddict()
            .AddCore(core =>
            {
                core.UseEntityFrameworkCore().UseDbContext<HuiaDbContext>();
            });

        builder.AddServer(server =>
        {
            server.SetAuthorizationEndpointUris("connect/authorize")
                  .SetPushedAuthorizationEndpointUris("connect/par")
                  .SetTokenEndpointUris("connect/token")
                  .SetEndSessionEndpointUris("connect/logout")
                  .SetUserInfoEndpointUris("connect/userinfo")
                  .SetIntrospectionEndpointUris("connect/introspect")
                  .SetRevocationEndpointUris("connect/revoke")
                  .SetDeviceAuthorizationEndpointUris("connect/device")
                  .SetEndUserVerificationEndpointUris("connect/verify")
                  .SetConfigurationEndpointUris(".well-known/openid-configuration")
                  .SetJsonWebKeySetEndpointUris(".well-known/jwks");

            server.AllowAuthorizationCodeFlow()
                  .AllowRefreshTokenFlow()
                  .AllowClientCredentialsFlow()
                  .AllowDeviceAuthorizationFlow();

            server.RequireProofKeyForCodeExchange();

            server.RegisterScopes(Scopes.OpenId, Scopes.Email, Scopes.Profile, Scopes.Roles, Scopes.OfflineAccess);

            // Throw-away keys: real signing/validation is per-tenant (see the custom handlers).
            server.AddEphemeralEncryptionKey()
                  .AddEphemeralSigningKey();

            server.DisableAccessTokenEncryption();

            // Note: OpenIddict's status-code-pages integration is deliberately NOT enabled — the
            // account UI's UseStatusCodePagesWithReExecute renders a generic branded page, and
            // protocol errors must keep OpenIddict's own (machine-readable) error responses.
            var aspNetCore = server.UseAspNetCore()
                .EnableAuthorizationEndpointPassthrough()
                .EnableTokenEndpointPassthrough()
                .EnableEndSessionEndpointPassthrough()
                .EnableUserInfoEndpointPassthrough()
                .EnableEndUserVerificationEndpointPassthrough();

            if (relaxTransport)
            {
                aspNetCore.DisableTransportSecurityRequirement();
            }

            // Per-tenant key injection: sign with the tenant's rotated key, validate the server's own
            // token-consuming endpoints against the tenant key ring, and serve the tenant JWKS.
            server.AddEventHandler(HuiaTenantSigningKeyHandler.Descriptor);
            server.AddEventHandler(HuiaTenantServerTokenValidationHandler.Descriptor);
            server.AddEventHandler(HuiaTenantJwksHandler.Descriptor);
        });

        builder.AddValidation(validation =>
        {
            validation.UseLocalServer();
            validation.UseAspNetCore();

            validation.AddEventHandler(HuiaTenantTokenValidationHandler.Descriptor);
        });

        if (anyExternalLogin)
        {
            builder.AddClient(client =>
            {
                client.AllowAuthorizationCodeFlow();

                client.UseDataProtection();

                // OpenIddict refuses an interactive client with no encryption key even when Data
                // Protection is in use; this pair only satisfies the guard. State/nonce tokens stay in
                // the Data Protection format and survive restarts.
                client.AddEphemeralEncryptionKey()
                      .AddEphemeralSigningKey();

                client.UseSystemNetHttp();

                // RP-initiated logout: a federated Huia session ends the upstream provider's session too.
                client.SetPostLogoutRedirectionEndpointUris("signout-callback-oidc");

                var clientAspNetCore = client.UseAspNetCore()
                    .EnableRedirectionEndpointPassthrough()
                    .EnablePostLogoutRedirectionEndpointPassthrough();

                if (relaxTransport)
                {
                    clientAspNetCore.DisableTransportSecurityRequirement();
                }

                RegisterExternalProviders(client, options);
            });
        }

        // Tenant-scoped application store: clients bound to other tenants are invisible to authorize/token.
        services.RemoveAll(typeof(IOpenIddictApplicationStore<OpenIddictEntityFrameworkCoreApplication>));
        services.AddScoped<IOpenIddictApplicationStore<OpenIddictEntityFrameworkCoreApplication>, HuiaOpenIddictApplicationStore>();

        // Tenant-scoped scope store: custom scopes owned by other tenants are invisible to authorize.
        services.RemoveAll(typeof(IOpenIddictScopeStore<OpenIddictEntityFrameworkCoreScope>));
        services.AddScoped<IOpenIddictScopeStore<OpenIddictEntityFrameworkCoreScope>, HuiaOpenIddictScopeStore>();

        return services;
    }

    /// <summary>
    /// Adds one OpenIddict client registration per (tenant, provider). The registration id is
    /// <c>{tenant}:{provider}</c> and the redirect URI is the relative <c>signin-{provider}</c>, which the
    /// base-path rebasing turns into <c>/{tenant}/signin-{provider}</c>.
    /// </summary>
    private static void RegisterExternalProviders(OpenIddictClientBuilder client, HuiaOptions options)
    {
        foreach (var (tenantId, tenant) in options.Tenants)
        {
            var external = tenant.Authentication.Passwordless.ExternalLogin;
            if (external is null)
            {
                continue;
            }

            foreach (var provider in external.Providers)
            {
                if (provider.Kind != ExternalProviderKind.OpenIdConnect)
                {
                    // The vendor providers (Google, GitHub, Microsoft) go through UseWebProviders();
                    // that wiring is added with the sample provider suite.
                    continue;
                }

                var registration = new OpenIddictClientRegistration
                {
                    RegistrationId = $"{tenantId}:{provider.Name}",
                    Issuer = new Uri(provider.Authority!, UriKind.Absolute),
                    ClientId = provider.ClientId,
                    ClientSecret = provider.ClientSecret,
                    RedirectUri = new Uri($"signin-{provider.Name.ToLowerInvariant()}", UriKind.Relative),
                    PostLogoutRedirectUri = new Uri("signout-callback-oidc", UriKind.Relative),
                };

                registration.Scopes.Add(Scopes.OpenId);
                foreach (var scope in provider.Scopes)
                {
                    registration.Scopes.Add(scope);
                }

                client.AddRegistration(registration);
            }
        }
    }
}
