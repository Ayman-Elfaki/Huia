using Huia.DependencyInjection;
using Huia.Entities;
using Huia.Headless.Identity;
using Huia.Headless.Multitenancy;
using Huia.Headless.Services;
using Huia.Identity;
using Huia.Multitenancy;
using Huia.Options;
using Huia.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers the single-tenant, bearer-token flavor of Huia: ASP.NET Core Identity's own API endpoints
/// (<c>AddApiEndpoints</c> / <c>MapIdentityApi</c> — register, login, refresh, email confirmation,
/// password reset, 2FA, profile) plus passkeys and passwordless SMS phone login, on the shared
/// <see cref="HuiaUserManager{TUser}"/> / <see cref="HuiaSignInManager{TUser}"/> /
/// <see cref="HuiaPasskeyRegistrar{TUser}"/> core. No multi-tenancy, no OpenIddict — call
/// <c>.AddHuiaOpenId()</c> from <c>Huia.OpenId</c> instead for that.
/// </summary>
public static class HuiaHeadlessServiceCollectionExtensions
{
    /// <summary>
    /// Registers the single-tenant flavor of Huia. Call after <c>AddHuia()</c> and
    /// <c>.AddEntityFrameworkCoreStores&lt;TContext&gt;()</c>.
    /// </summary>
    /// <param name="builder">The Huia builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <exception cref="InvalidOperationException">The host configured anything other than exactly one tenant.</exception>
    public static IHuiaBuilder AddHuiaHeadless(this IHuiaBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;
        var options = builder.Options;

        if (options.Tenants.Count != 1)
        {
            throw new InvalidOperationException(
                "Huia.Headless is single-tenant: configure exactly one tenant with AddTenant(...). " +
                $"{options.Tenants.Count} were configured.");
        }

        var tenantId = options.Tenants.Keys.Single();
        var tenant = options.Tenants[tenantId];
        services.AddSingleton<IHuiaTenantContext>(new HuiaSingleTenantContext(tenantId));
        services.AddHttpContextAccessor();

        // Bearer tokens (ASP.NET Core Identity's own scheme), not cookies — MapIdentityApi issues and
        // validates these directly; there is no interactive sign-in UI to protect with a cookie.
        var authentication = services.AddAuthentication(IdentityConstants.BearerScheme)
            .AddBearerToken(IdentityConstants.BearerScheme);
        services.AddAuthorization();

        if (tenant.Authentication.IsExternalLoginEnabled)
        {
            var external = tenant.Authentication.External!;
            if (external.AllowedReturnUrlPrefixes.Count == 0)
            {
                throw new InvalidOperationException(
                    "Huia.Headless external login needs at least one ExternalLoginOptions.AllowReturnUrlPrefix(...) " +
                    "— the app's own origin(s) the challenge's returnUrl is allowed to point at.");
            }

            // The intermediate hop between the provider's callback and our own dispatch endpoint — the
            // same role IdentityConstants.ExternalScheme plays in the classic ASP.NET Core Identity UI,
            // just without the interactive UI. Every leg of this hop (the provider's callback path and
            // our dispatch endpoint) is same-origin with Huia.Headless itself, so the default cookie
            // options are fine — only the *final* redirect (dispatch -> the app's own frontend) crosses
            // an origin, and that leg carries a one-time code in the URL, never a cookie.
            authentication.AddCookie(IdentityConstants.ExternalScheme);

            foreach (var provider in external.Providers)
            {
                RegisterExternalProvider(authentication, provider);
            }
        }

        // Always registered — regardless of whether external login is enabled — because the external
        // endpoints (ExternalEndpoints.cs) are always mapped by MapHuiaHeadlessEndpoints(), and minimal
        // API's [FromServices]-vs-[FromBody] parameter inference needs IExternalLoginFlowStore to be a
        // known service at endpoint-build time even for a host that never configures a provider (those
        // endpoints just 404 at request time instead, the same way PasskeyEndpoints does).
        services.TryAddSingleton<IExternalLoginFlowStore, ExternalLoginFlowStore>();

        new IdentityBuilder(typeof(HuiaUser), typeof(HuiaRole), services)
            .AddApiEndpoints()
            .AddUserManager<HuiaUserManager>()
            .AddSignInManager<HuiaSignInManager<HuiaUser>>();

        // AddUserManager<HuiaUserManager>() bridges UserManager<HuiaUser> to the concrete type, but not
        // the generic Huia.Identity.HuiaUserManager<HuiaUser> in between — HuiaPasskeyRegistrar<TUser>
        // depends on that directly (for the phone/external members, not just the ASP.NET Core surface).
        services.AddScoped<Huia.Identity.HuiaUserManager<HuiaUser>>(sp => sp.GetRequiredService<HuiaUserManager>());
        services.AddScoped<HuiaPasskeyRegistrar<HuiaUser>>();

        // Passwordless SMS (always on; hosts replace the SMS / CAPTCHA senders) — the same core
        // services Huia.OpenId registers, shared rather than duplicated.
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IPhoneNumberService, PhoneNumberService>();
        services.TryAddSingleton<IOtpRateLimiter, InMemoryOtpRateLimiter>();
        services.TryAddSingleton<IPhoneLoginRateLimiter, InMemoryPhoneLoginRateLimiter>();
        services.TryAddSingleton<IPendingPhoneSignup, PendingPhoneSignup>();
        services.TryAddSingleton<IPhoneLoginFlowStore, PhoneLoginFlowStore>();
        services.TryAddScoped<IOtpService<HuiaUser>, OtpService<HuiaUser>>();
        services.TryAddScoped<ISmsSender, HuiaSmsSender>();
        services.TryAddScoped<ICaptchaVerifier, NullCaptchaVerifier>();

        return builder;
    }

    /// <summary>
    /// Registers one configured provider as a classic ASP.NET Core remote-authentication scheme, named
    /// <see cref="ExternalProviderRegistration.Name"/> and signing into <see cref="IdentityConstants.ExternalScheme"/>
    /// — the same intermediate hand-off <c>Huia.OpenId</c> gets from the OpenIddict client, just via the
    /// framework's own handlers instead (Headless has no OpenIddict dependency to reuse for this).
    /// </summary>
    private static void RegisterExternalProvider(AuthenticationBuilder authentication, ExternalProviderRegistration provider)
    {
        switch (provider.Kind)
        {
            case ExternalProviderKind.Google:
                authentication.AddGoogle(provider.Name, o =>
                {
                    o.ClientId = provider.ClientId;
                    o.ClientSecret = provider.ClientSecret;
                    o.SignInScheme = IdentityConstants.ExternalScheme;
                    o.CallbackPath = $"/signin-{provider.Name}";
                    foreach (var scope in provider.Scopes)
                    {
                        o.Scope.Add(scope);
                    }
                });
                break;

            case ExternalProviderKind.MicrosoftAccount:
                authentication.AddMicrosoftAccount(provider.Name, o =>
                {
                    o.ClientId = provider.ClientId;
                    o.ClientSecret = provider.ClientSecret;
                    o.SignInScheme = IdentityConstants.ExternalScheme;
                    o.CallbackPath = $"/signin-{provider.Name}";
                    foreach (var scope in provider.Scopes)
                    {
                        o.Scope.Add(scope);
                    }
                });
                break;

            case ExternalProviderKind.GitHub:
                // No first-party ASP.NET Core handler for GitHub — its OAuth endpoints are stable and
                // well-known, so a generic AddOAuth needs no extra package.
                authentication.AddOAuth(provider.Name, o =>
                {
                    o.ClientId = provider.ClientId;
                    o.ClientSecret = provider.ClientSecret;
                    o.SignInScheme = IdentityConstants.ExternalScheme;
                    o.CallbackPath = $"/signin-{provider.Name}";
                    o.AuthorizationEndpoint = "https://github.com/login/oauth/authorize";
                    o.TokenEndpoint = "https://github.com/login/oauth/access_token";
                    o.UserInformationEndpoint = "https://api.github.com/user";
                    o.Scope.Add("read:user");
                    foreach (var scope in provider.Scopes)
                    {
                        o.Scope.Add(scope);
                    }

                    o.ClaimActions.MapJsonKey(System.Security.Claims.ClaimTypes.NameIdentifier, "id");
                    o.ClaimActions.MapJsonKey(System.Security.Claims.ClaimTypes.Name, "name");
                    o.ClaimActions.MapJsonKey("urn:github:login", "login");
                    o.ClaimActions.MapJsonKey(System.Security.Claims.ClaimTypes.Email, "email");
                    o.Events = new OAuthEvents
                    {
                        OnCreatingTicket = async context =>
                        {
                            using var request = new HttpRequestMessage(HttpMethod.Get, context.Options.UserInformationEndpoint);
                            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", context.AccessToken);
                            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                            using var response = await context.Backchannel.SendAsync(request, context.HttpContext.RequestAborted);
                            response.EnsureSuccessStatusCode();
                            using var user = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(context.HttpContext.RequestAborted));
                            context.RunClaimActions(user.RootElement);
                        },
                    };
                });
                break;

            case ExternalProviderKind.OpenIdConnect:
            default:
                authentication.AddOpenIdConnect(provider.Name, o =>
                {
                    o.ClientId = provider.ClientId;
                    o.ClientSecret = provider.ClientSecret;
                    o.Authority = provider.Authority;
                    o.SignInScheme = IdentityConstants.ExternalScheme;
                    o.CallbackPath = $"/signin-{provider.Name}";
                    o.ResponseType = "code";
                    foreach (var scope in provider.Scopes)
                    {
                        o.Scope.Add(scope);
                    }
                });
                break;
        }
    }
}
