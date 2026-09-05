using System.Net;
using Finbuckle.MultiTenant.Abstractions;
using Huia.AspNetCore.Identity;
using Huia.AspNetCore.Multitenancy;
using Huia.EntityFrameworkCore;
using Huia.EntityFrameworkCore.Entities;
using Huia.EntityFrameworkCore.Multitenancy;
using Huia.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Huia.IntegrationTests.Infrastructure;

/// <summary>
/// An in-process Huia host: a bare <see cref="HostBuilder"/> with the test server, a SQLite database on a
/// single shared open connection, and the schema created by a hosted service that is registered
/// <em>before</em> <c>AddHuia</c> so it runs first.
/// </summary>
public sealed class HuiaTestHost : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly SqliteConnection _connection;

    private HuiaTestHost(IHost host, SqliteConnection connection, CapturingSmsSender sms, CapturingEmailSender email, CapturingEventCollector events)
    {
        _host = host;
        _connection = connection;
        Sms = sms;
        Email = email;
        Events = events;
        Client = CreateClient();
    }

    /// <summary>A cookie-isolated client that does not follow redirects (tests inspect each hop).</summary>
    public HttpClient Client { get; }

    /// <summary>The capturing SMS sender wired into this host.</summary>
    public CapturingSmsSender Sms { get; }

    /// <summary>The capturing email sender wired into this host.</summary>
    public CapturingEmailSender Email { get; }

    /// <summary>Collects every domain event raised during the test.</summary>
    public CapturingEventCollector Events { get; }

    /// <summary>The root service provider (for resolving services in assertions).</summary>
    public IServiceProvider Services => _host.Services;

    /// <summary>The base address the test server answers on.</summary>
    public Uri BaseAddress => _host.GetTestServer().BaseAddress;

    /// <summary>Creates a fresh client with its own cookie jar. Redirects are not followed.</summary>
    /// <returns>A new client.</returns>
    public HttpClient CreateClient()
    {
        var server = _host.GetTestServer();
        var handler = new CookieForwardingHandler(new CookieContainer())
        {
            InnerHandler = server.CreateHandler(),
        };
        return new HttpClient(handler) { BaseAddress = server.BaseAddress };
    }

    /// <summary>Starts a host. <paramref name="configureOptions"/> runs after the two default tenants are added.</summary>
    /// <param name="configureOptions">Extra options configuration.</param>
    /// <param name="configureEndpoints">Extra endpoints to map alongside the built-in probe.</param>
    /// <param name="configureBuilder">Runs against the <c>IHuiaBuilder</c> returned by <c>AddHuia</c> (feature opt-ins).</param>
    /// <param name="timeProvider">A clock to install before <c>AddHuia</c>.</param>
    /// <returns>The started host.</returns>
    public static async Task<HuiaTestHost> StartAsync(
        Action<HuiaOptionsBuilder>? configureOptions = null,
        Action<IEndpointRouteBuilder>? configureEndpoints = null,
        Action<Huia.AspNetCore.DependencyInjection.IHuiaBuilder>? configureBuilder = null,
        TimeProvider? timeProvider = null)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var sms = new CapturingSmsSender();
        var email = new CapturingEmailSender();
        var eventCollector = new CapturingEventCollector();

        var host = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    if (timeProvider is not null)
                    {
                        services.AddSingleton(timeProvider);
                    }

                    services.AddDbContext<HuiaDbContext>(options => options.UseSqlite(connection));
                    services.AddSingleton<IHostedService, SchemaInitializer>();

                    var builder = services.AddHuia(huia =>
                    {
                        huia.UseIssuer("https://id.huia.test");
                        huia.DisableTransportSecurityRequirement();
                        huia.ConfigureKeys(keys =>
                        {
                            keys.EnableBackgroundJobs = false;
                            keys.KeySize = 2048;
                        });
                        huia.ConfigureCleanup(cleanup => cleanup.EnableBackgroundJobs = false);
                        huia.AddTenant("master", tenant => tenant.Authentication.UseEmailAndPasswordLogin());
                        huia.AddTenant("acme", tenant =>
                        {
                            tenant.Authentication.UseEmailAndPasswordLogin(password => password.RequireConfirmedEmail = false);
                            tenant.Authentication.UsePasskeyLogin();
                            tenant.Branding.DisplayName = "Acme Corp";
                        });
                        huia.AddTenant("phone", tenant =>
                            tenant.Authentication.UsePhoneLogin());
                        huia.AddTenant("phone-auto", tenant =>
                            tenant.Authentication.UsePhoneLogin(phone => phone.AllowAutoProvisioning = true));
                        huia.AddTenant("signup", tenant =>
                        {
                            tenant.Authentication.UseEmailAndPasswordLogin(password =>
                            {
                                password.RequireConfirmedEmail = true;
                                password.AllowSelfServiceRegistration = true;
                            });
                        });
                        huia.AddTenant("no-signup", tenant =>
                        {
                            tenant.Authentication.UseEmailAndPasswordLogin();
                            tenant.DisableRegistration();
                        });
                        huia.AddTenant("consumer", tenant =>
                        {
                            tenant.Authentication.UseEmailAndPasswordLogin(password => password.RequireConfirmedEmail = false);
                            tenant.Authentication.UseExternalLogin(ext =>
                                ext.AddOpenIdConnect("HuiaExternal", "consumer-client", "consumer-secret", "https://partner.example.test", p =>
                                {
                                    p.DisplayName = "Partner";
                                    p.Scopes.Add("profile");
                                    p.Scopes.Add("email");
                                }));
                        });
                        configureOptions?.Invoke(huia);
                    });
                    builder.AddHuiaUi();
                    services.AddSingleton(sms);
                    services.AddScoped<Huia.AspNetCore.Services.ISmsSender>(_ => sms);
                    services.AddSingleton(email);
                    services.AddScoped<Huia.AspNetCore.Emails.IHuiaEmailSender>(_ => email);
                    services.AddSingleton(eventCollector);
                    RegisterEventCollector(services, eventCollector);
                    configureBuilder?.Invoke(builder);
                });
                web.Configure(app =>
                {
                    app.UseHuia();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapHuiaEndpoints();
                        endpoints.MapHuiaAdminEndpoints()
                            .RequireAuthorization(policy => policy.RequireTenants("master").RequireRole(HuiaConstants.Roles.Administrator));
                        endpoints.MapGet("/probe", (HttpContext context, IMultiTenantContextAccessor tenantAccessor) => Results.Ok(new ProbeResult(
                            tenantAccessor.CurrentTenantId(),
                            context.Request.PathBase.Value ?? string.Empty,
                            context.Request.Path.Value ?? string.Empty)));
                        configureEndpoints?.Invoke(endpoints);
                    });
                });
            })
            .Build();

        await host.StartAsync();
        return new HuiaTestHost(host, connection, sms, email, eventCollector);
    }

    private static void RegisterEventCollector(IServiceCollection services, CapturingEventCollector collector)
    {
        void Add<T>() where T : Huia.Events.IHuiaEvent =>
            services.AddSingleton<Huia.Events.IHuiaEventHandler<T>>(new CollectingEventHandler<T>(collector));
        Add<Huia.Events.UserRegisteredEvent>();
        Add<Huia.Events.UserLoggedInEvent>();
        Add<Huia.Events.PasswordChangedEvent>();
        Add<Huia.Events.OtpRequestedEvent>();
        Add<Huia.Events.OtpVerifiedEvent>();
        Add<Huia.Events.PhoneChangedEvent>();
        Add<Huia.Events.PasskeyRegisteredEvent>();
        Add<Huia.Events.PasskeyRemovedEvent>();
    }

    /// <summary>Seeds a confidential client-credentials application bound to a tenant.</summary>
    /// <param name="tenantId">The owning tenant (written to <c>Properties["huia:tenant"]</c>).</param>
    /// <param name="clientId">The client id.</param>
    /// <param name="clientSecret">The client secret.</param>
    /// <param name="scopes">Scopes the client may request.</param>
    public async Task SeedMachineClientAsync(string tenantId, string clientId, string clientSecret, params string[] scopes)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientSecret = clientSecret,
            ClientType = ClientTypes.Confidential,
            Permissions =
            {
                Permissions.Endpoints.Token,
                Permissions.GrantTypes.ClientCredentials,
            },
        };

        foreach (var scopeName in scopes)
        {
            descriptor.Permissions.Add(Permissions.Prefixes.Scope + scopeName);
        }

        descriptor.Properties["huia:tenant"] = System.Text.Json.JsonSerializer.SerializeToElement(tenantId);
        await manager.CreateAsync(descriptor);
    }

    /// <summary>Seeds an interactive authorization-code + PKCE client (public by default).</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="clientId">The client id.</param>
    /// <param name="redirectUri">The single redirect URI.</param>
    /// <param name="clientSecret">A secret to make the client confidential; omit for a public SPA/native client.</param>
    /// <param name="requirePushedAuthorization">When true, adds the <c>ft:par</c> requirement.</param>
    public async Task SeedInteractiveClientAsync(
        string tenantId, string clientId, string redirectUri, string? clientSecret = null, bool requirePushedAuthorization = false)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientSecret = clientSecret,
            ClientType = clientSecret is null ? ClientTypes.Public : ClientTypes.Confidential,
            RedirectUris = { new Uri(redirectUri) },
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.PushedAuthorization,
                Permissions.Endpoints.Token,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
                Permissions.Scopes.Email,
                Permissions.Scopes.Profile,
                Permissions.Scopes.Roles,
                Permissions.Prefixes.Scope + Scopes.OfflineAccess,
            },
            Requirements = { Requirements.Features.ProofKeyForCodeExchange },
        };

        if (requirePushedAuthorization)
        {
            descriptor.Requirements.Add(Requirements.Features.PushedAuthorizationRequests);
        }

        descriptor.Properties["huia:tenant"] = System.Text.Json.JsonSerializer.SerializeToElement(tenantId);
        await manager.CreateAsync(descriptor);
    }

    /// <summary>
    /// Seeds a phone-login user (no password, no email; the username is the E.164 number) and returns
    /// its id.
    /// </summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="phoneNumber">The confirmed E.164 phone number, also used as the username.</param>
    /// <returns>The new user's id.</returns>
    public async Task<string> SeedPhoneUserAsync(string tenantId, string phoneNumber)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        using (HuiaTenantScope.Enter(scope.ServiceProvider, tenantId))
        {
            var userManager = scope.ServiceProvider.GetRequiredService<HuiaUserManager>();
            var user = new HuiaUser
            {
                TenantId = tenantId,
                UserName = phoneNumber,
                PhoneNumber = phoneNumber,
                PhoneNumberConfirmed = true,
                FirstName = "Test",
                LastName = "User",
            };

            var result = await userManager.CreateAsync(user);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    "Seeding phone user failed: " + string.Join("; ", result.Errors.Select(e => e.Description)));
            }

            return user.Id;
        }
    }

    /// <summary>Seeds a user for a tenant and returns its id.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="email">The email / username.</param>
    /// <param name="password">The password, or <see langword="null"/> for a passwordless account.</param>
    /// <param name="emailConfirmed">Whether the email starts confirmed.</param>
    /// <param name="phoneNumber">An optional confirmed phone number.</param>
    /// <returns>The new user's id.</returns>
    public async Task<string> SeedUserAsync(
        string tenantId,
        string email,
        string? password,
        bool emailConfirmed = true,
        string? phoneNumber = null)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        using (HuiaTenantScope.Enter(scope.ServiceProvider, tenantId))
        {
            var userManager = scope.ServiceProvider.GetRequiredService<HuiaUserManager>();
            var user = new HuiaUser
            {
                TenantId = tenantId,
                UserName = email,
                Email = email,
                EmailConfirmed = emailConfirmed,
                FirstName = "Test",
                LastName = "User",
                PhoneNumber = phoneNumber,
                PhoneNumberConfirmed = phoneNumber is not null,
            };

            var result = password is null
                ? await userManager.CreateAsync(user)
                : await userManager.CreateAsync(user, password);

            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    "Seeding user failed: " + string.Join("; ", result.Errors.Select(e => e.Description)));
            }

            return user.Id;
        }
    }

    /// <summary>Runs <paramref name="work"/> with a tenant-scoped <see cref="UserManager{HuiaUser}"/>.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="tenantId">The tenant to enter.</param>
    /// <param name="work">The callback.</param>
    /// <returns>The callback's result.</returns>
    public async Task<T> WithUserManagerAsync<T>(string tenantId, Func<HuiaUserManager, Task<T>> work)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        using (HuiaTenantScope.Enter(scope.ServiceProvider, tenantId))
        {
            var userManager = scope.ServiceProvider.GetRequiredService<HuiaUserManager>();
            return await work(userManager);
        }
    }

    /// <summary>Runs <paramref name="work"/> with a tenant-scoped <see cref="HuiaFlowIdentity"/> for <paramref name="flow"/>.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="tenantId">The tenant to enter.</param>
    /// <param name="flow">The authentication flow.</param>
    /// <param name="work">The callback.</param>
    /// <returns>The callback's result.</returns>
    public async Task<T> WithFlowIdentityAsync<T>(string tenantId, HuiaAuthFlow flow, Func<HuiaFlowIdentity, Task<T>> work)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        using (HuiaTenantScope.Enter(scope.ServiceProvider, tenantId))
        {
            var factory = scope.ServiceProvider.GetRequiredService<IHuiaFlowIdentityFactory>();
            return await work(factory.Create(flow));
        }
    }

    /// <summary>Adds an external login to a user, making it an "external" account.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="userId">The user.</param>
    /// <param name="provider">The login provider name.</param>
    /// <param name="providerKey">The provider's subject key.</param>
    public async Task AddExternalLoginAsync(string tenantId, string userId, string provider, string providerKey)
    {
        await WithUserManagerAsync(tenantId, async userManager =>
        {
            var user = await userManager.FindByIdAsync(userId) ?? throw new InvalidOperationException("user not found");
            var result = await userManager.AddLoginAsync(user, new UserLoginInfo(provider, providerKey, provider));
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    "Adding external login failed: " + string.Join("; ", result.Errors.Select(e => e.Description)));
            }

            return true;
        });
    }

    /// <summary>Ensures a role exists for a tenant and adds a user to it.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="userId">The user to add.</param>
    /// <param name="role">The role name.</param>
    public async Task AssignRoleAsync(string tenantId, string userId, string role)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        using (HuiaTenantScope.Enter(scope.ServiceProvider, tenantId))
        {
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<HuiaRole>>();
            var userManager = scope.ServiceProvider.GetRequiredService<HuiaUserManager>();

            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new HuiaRole(role) { TenantId = tenantId });
            }

            var user = await userManager.FindByIdAsync(userId) ?? throw new InvalidOperationException("user not found");
            await userManager.AddToRoleAsync(user, role);
        }
    }

    /// <summary>Result shape returned by the built-in <c>/probe</c> endpoint.</summary>
    /// <param name="Tenant">The resolved tenant, or <see langword="null"/>.</param>
    /// <param name="PathBase">The request path base after Finbuckle rebasing.</param>
    /// <param name="Path">The request path after Finbuckle rebasing.</param>
    public sealed record ProbeResult(string? Tenant, string PathBase, string Path);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
        await _connection.DisposeAsync();
    }

    private sealed class SchemaInitializer(IServiceProvider services) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            using var scope = services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<HuiaDbContext>();
            await context.Database.EnsureCreatedAsync(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>Persists Set-Cookie across calls; the raw test-server handler does not.</summary>
    private sealed class CookieForwardingHandler(CookieContainer cookies) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var header = cookies.GetCookieHeader(uri);
            if (!string.IsNullOrEmpty(header))
            {
                request.Headers.Add("Cookie", header);
            }

            var response = await base.SendAsync(request, cancellationToken);

            if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
            {
                foreach (var setCookie in setCookies)
                {
                    cookies.SetCookies(uri, setCookie);
                }
            }

            return response;
        }
    }
}
