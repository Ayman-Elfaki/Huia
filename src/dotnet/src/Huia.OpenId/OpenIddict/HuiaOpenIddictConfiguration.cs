using System.Net;
using System.Net.Sockets;
using Huia.OpenId.DependencyInjection;
using Huia.OpenId.OpenIddict.Handlers;
using Huia.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using OpenIddict.Abstractions;
using OpenIddict.Client;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Huia.OpenId.OpenIddict;

/// <summary>
/// Registers the OpenIddict server, validation and (when any tenant uses external login) client
/// features. The server is given a throw-away ephemeral key pair purely to satisfy OpenIddict's
/// start-up guards; the keys that actually sign and validate tokens are the per-tenant rotated keys,
/// injected by the custom handlers in <see cref="Handlers"/>.
/// </summary>
internal static class HuiaOpenIddictConfiguration
{
    public static IServiceCollection AddHuiaOpenIddict(
        this IServiceCollection services, HuiaOptions options, HuiaOpenIdConfigurationBuilder? configBuilder = null)
    {
        var relaxTransport = options.DisableTransportSecurityRequirement;
        var anyExternalLogin = options.Tenants.Values.Any(t => t.Authentication.IsExternalLoginEnabled);

        var builder = services.AddOpenIddict();

        if (configBuilder is not null)
        {
            foreach (var configure in configBuilder.OpenIddictConfigurations)
            {
                configure(builder);
            }
        }

        builder.AddCore(core =>
        {
            // EF Core persistence is wired by Huia.OpenId.EntityFrameworkCore's
            // AddEntityFrameworkCoreStores<>() via an IOpenIddictCoreBuilder action stored
            // in the options. This AddCore() call sets up the OpenIddict DI skeleton only.

            if (options.Cleanup.EnableBackgroundJobs)
            {
                core.UseQuartz(quartz =>
                {
                    if (!options.Cleanup.PruneAuthorizations)
                    {
                        quartz.DisableAuthorizationPruning();
                    }

                    if (!options.Cleanup.PruneTokens)
                    {
                        quartz.DisableTokenPruning();
                    }

                    quartz.SetMaximumRefireCount(options.Cleanup.MaximumRefireCount);
                    quartz.SetMinimumAuthorizationLifespan(options.Cleanup.MinimumAuthorizationLifespan);
                    quartz.SetMinimumTokenLifespan(options.Cleanup.MinimumTokenLifespan);
                });
            }

            if (configBuilder is not null)
            {
                foreach (var configure in configBuilder.OpenIddictCoreConfigurations)
                {
                    configure(core);
                }
            }
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

            if (configBuilder is not null)
            {
                foreach (var configure in configBuilder.OpenIddictServerConfigurations)
                {
                    configure(server);
                }
            }
        });

        builder.AddValidation(validation =>
        {
            validation.UseLocalServer();
            validation.UseAspNetCore();

            validation.AddEventHandler(HuiaTenantTokenValidationHandler.Descriptor);

            if (configBuilder is not null)
            {
                foreach (var configure in configBuilder.OpenIddictValidationConfigurations)
                {
                    configure(validation);
                }
            }
        });

        if (anyExternalLogin || (configBuilder?.OpenIddictClientConfigurations.Count > 0))
        {
            builder.AddClient(client =>
            {
                client.AllowAuthorizationCodeFlow();

                client.UseDataProtection();

                client.AddEphemeralEncryptionKey()
                      .AddEphemeralSigningKey();

                client.UseSystemNetHttp();

                client.SetPostLogoutRedirectionEndpointUris("signout-callback-oidc");

                var clientAspNetCore = client.UseAspNetCore()
                    .EnableRedirectionEndpointPassthrough()
                    .EnablePostLogoutRedirectionEndpointPassthrough();

                if (relaxTransport)
                {
                    clientAspNetCore.DisableTransportSecurityRequirement();
                }

                RegisterExternalProviders(client, options);

                if (configBuilder is not null)
                {
                    foreach (var configure in configBuilder.OpenIddictClientConfigurations)
                    {
                        configure(client);
                    }
                }
            });

            services.ConfigureAll<HttpClientFactoryOptions>(httpOptions =>
            {
                httpOptions.HttpMessageHandlerBuilderActions.Add(b =>
                {
                    if (b.Name?.StartsWith("OpenIddict", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        b.PrimaryHandler = CreateRobustHttpClientHandler();
                    }
                });
            });
        }

        return services;
    }

    /// <summary>
    /// Adds one OpenIddict client registration per (tenant, provider).
    /// </summary>
    private static void RegisterExternalProviders(OpenIddictClientBuilder client, HuiaOptions options)
    {
        foreach (var (tenantId, tenant) in options.Tenants)
        {
            var external = tenant.Authentication.Find<ExternalLoginAuthenticationMethod>();
            if (external is null)
            {
                continue;
            }

            foreach (var provider in external.Providers)
            {
                var redirectUri = provider.RedirectUri ?? new Uri($"signin-{provider.Name.ToLowerInvariant()}", UriKind.Relative);
                var postLogoutUri = new Uri("signout-callback-oidc", UriKind.Relative);

                if (provider.Kind == ExternalProviderKind.Google)
                {
                    client.UseWebProviders().AddGoogle(google =>
                    {
                        google.SetClientId(provider.ClientId)
                              .SetClientSecret(provider.ClientSecret)
                              .SetRedirectUri(redirectUri)
                              .SetPostLogoutRedirectUri(postLogoutUri)
                              .SetRegistrationId($"{tenantId}:{provider.Name}");

                        google.AddScopes(Scopes.OpenId);
                        foreach (var scope in provider.Scopes)
                        {
                            google.AddScopes(scope);
                        }
                    });
                    continue;
                }

                if (provider.Kind == ExternalProviderKind.GitHub)
                {
                    client.UseWebProviders().AddGitHub(github =>
                    {
                        github.SetClientId(provider.ClientId)
                              .SetClientSecret(provider.ClientSecret)
                              .SetRedirectUri(redirectUri)
                              .SetPostLogoutRedirectUri(postLogoutUri)
                              .SetRegistrationId($"{tenantId}:{provider.Name}");

                        foreach (var scope in provider.Scopes)
                        {
                            github.AddScopes(scope);
                        }
                    });
                    continue;
                }

                if (provider.Kind == ExternalProviderKind.MicrosoftAccount)
                {
                    client.UseWebProviders().AddMicrosoft(ms =>
                    {
                        ms.SetClientId(provider.ClientId)
                          .SetClientSecret(provider.ClientSecret)
                          .SetRedirectUri(redirectUri)
                          .SetPostLogoutRedirectUri(postLogoutUri)
                          .SetRegistrationId($"{tenantId}:{provider.Name}");

                        ms.AddScopes(Scopes.OpenId);
                        foreach (var scope in provider.Scopes)
                        {
                            ms.AddScopes(scope);
                        }
                    });
                    continue;
                }

                if (provider.Kind != ExternalProviderKind.OpenIdConnect)
                {
                    continue;
                }

                var oidcProvider = (OpenIdConnectExternalProvider)provider;
                var registration = new OpenIddictClientRegistration
                {
                    RegistrationId = $"{tenantId}:{provider.Name}",
                    Issuer = new Uri(oidcProvider.Authority, UriKind.Absolute),
                    ClientId = provider.ClientId,
                    ClientSecret = provider.ClientSecret,
                    RedirectUri = redirectUri,
                    PostLogoutRedirectUri = postLogoutUri,
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

    private static HttpClientHandler CreateRobustHttpClientHandler()
    {
        var handler = new HttpClientHandler();
        var underlyingField = typeof(HttpClientHandler).GetField("_underlyingHandler", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (underlyingField?.GetValue(handler) is SocketsHttpHandler socketsHandler)
        {
            socketsHandler.ConnectCallback = async (context, cancellationToken) =>
            {
                if (IPAddress.TryParse(context.DnsEndPoint.Host, out var ip))
                {
                    var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                    try
                    {
                        await socket.ConnectAsync(ip, context.DnsEndPoint.Port, cancellationToken);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                }

                var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
                var sorted = addresses
                    .OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
                    .ToArray();

                Socket? connectedSocket = null;
                Exception? lastException = null;

                foreach (var address in sorted)
                {
                    var s = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                    try
                    {
                        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                        await s.ConnectAsync(address, context.DnsEndPoint.Port, linkedCts.Token);
                        connectedSocket = s;
                        break;
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        s.Dispose();
                        lastException = ex;
                    }
                }

                if (connectedSocket is null)
                {
                    throw lastException ?? new SocketException((int)SocketError.HostNotFound);
                }

                return new NetworkStream(connectedSocket, ownsSocket: true);
            };
        }

        return handler;
    }
}
