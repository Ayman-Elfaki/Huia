using System.Security.Claims;
using Finbuckle.MultiTenant.Abstractions;
using Huia.Events;
using Huia.Identity;
using Huia.Multitenancy;
using Huia.OpenId.Flows;
using Huia.OpenId.Identity;
using Huia.Options;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using OpenIddict.Abstractions;
using OpenIddict.Client.AspNetCore;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Huia.OpenId.Endpoints;

/// <summary>
/// The OAuth / OpenID Connect protocol endpoints. Implements the authorization-code (with PKCE),
/// refresh-token and client-credentials grants at the token endpoint, and the interactive
/// authorization endpoint that hands off to the account UI for sign-in.
/// </summary>
internal static class ConnectEndpoints
{
    public static RouteGroupBuilder MapHuiaConnectEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("connect");

        group.MapMethods("authorize", ["GET", "POST"], AuthorizeAsync).WithName("huia.connect.authorize");
        group.MapMethods("token", ["POST"], ExchangeAsync).WithName("huia.connect.token");
        group.MapMethods("userinfo", ["GET", "POST"], UserInfoAsync).WithName("huia.connect.userinfo");
        group.MapMethods("logout", ["GET", "POST"], LogoutAsync).WithName("huia.connect.logout");
        group.MapMethods("verify", ["GET", "POST"], VerifyAsync).WithName("huia.connect.verify");

        return group;
    }

    private static async Task<IResult> AuthorizeAsync(
        HttpContext context,
        SignInManager<HuiaUser> signInManager,
        UserManager<HuiaUser> userManager,
        IMultiTenantContextAccessor tenantAccessor)
    {
        var request = context.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenIddict server request could not be retrieved.");

        var authenticate = await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var forceLogin = request.HasPromptValue(PromptValues.Login);

        if (!authenticate.Succeeded || authenticate.Principal is null || forceLogin)
        {
            if (request.HasPromptValue(PromptValues.None))
            {
                return Results.Forbid(
                    authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.LoginRequired,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The user is not signed in.",
                    }));
            }

            var returnUrl = context.Request.PathBase + context.Request.Path + context.Request.QueryString;
            return Results.Challenge(
                new AuthenticationProperties { RedirectUri = returnUrl },
                [IdentityConstants.ApplicationScheme]);
        }

        var user = await userManager.GetUserAsync(authenticate.Principal)
            ?? throw new InvalidOperationException("The signed-in user could not be resolved.");

        var tenantId = tenantAccessor.RequireCurrentTenantId();
        var identity = new ClaimsIdentity(
            authenticationType: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
            nameType: Claims.Name,
            roleType: Claims.Role);

        identity.SetClaim(Claims.Subject, await userManager.GetUserIdAsync(user));
        identity.SetClaim(Claims.Email, await userManager.GetEmailAsync(user));
        identity.SetClaim(Claims.Name, await userManager.GetUserNameAsync(user));
        identity.SetClaim(Claims.PreferredUsername, await userManager.GetUserNameAsync(user));
        identity.SetClaim(Claims.GivenName, user.FirstName);
        identity.SetClaim(Claims.FamilyName, user.LastName);
        identity.SetClaim(HuiaConstants.ClaimTypes.Tenant, tenantId);

        foreach (var amr in authenticate.Principal.FindAll(HuiaConstants.ClaimTypes.AuthenticationMethod))
        {
            identity.AddClaim(new Claim(HuiaConstants.ClaimTypes.AuthenticationMethod, amr.Value));
        }

        foreach (var role in await userManager.GetRolesAsync(user))
        {
            identity.AddClaim(new Claim(Claims.Role, role));
        }

        identity.SetScopes(request.GetScopes());
        identity.SetDestinations(GetDestinations);

        return Results.SignIn(new ClaimsPrincipal(identity), properties: null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<IResult> VerifyAsync(
        HttpContext context,
        UserManager<HuiaUser> userManager,
        IMultiTenantContextAccessor tenantAccessor,
        IAntiforgery antiforgery)
    {
        var request = context.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenIddict server request could not be retrieved.");

        var authenticate = await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (!authenticate.Succeeded || authenticate.Principal is null)
        {
            var returnUrl = context.Request.PathBase + context.Request.Path + context.Request.QueryString;
            return Results.Challenge(
                new AuthenticationProperties { RedirectUri = returnUrl },
                [IdentityConstants.ApplicationScheme]);
        }

        var form = context.Request.HasFormContentType ? await context.Request.ReadFormAsync() : null;
        var accepted = form?.ContainsKey("submit.accept") == true;
        var denied = form?.ContainsKey("submit.deny") == true;

        // A GET (or a POST that carries no decision) hands off to the confirmation page.
        if (!accepted && !denied)
        {
            var userCode = request.UserCode ?? context.Request.Query["user_code"].ToString();
            var target = QueryHelpers.AddQueryString(
                $"{context.Request.PathBase}/identity/account/deviceverification", "user_code", userCode ?? string.Empty);
            return Results.Redirect(target);
        }

        await antiforgery.ValidateRequestAsync(context);

        if (denied)
        {
            return Results.Forbid(
                authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
                properties: new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.AccessDenied,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The authorization was denied by the end user.",
                }));
        }

        // The scopes requested during device authorization are carried by the user-code principal.
        var codeAuth = await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var scopes = codeAuth.Principal?.GetScopes() ?? request.GetScopes();

        var user = await userManager.GetUserAsync(authenticate.Principal)
            ?? throw new InvalidOperationException("The signed-in user could not be resolved.");
        var tenantId = tenantAccessor.RequireCurrentTenantId();

        var identity = new ClaimsIdentity(
            authenticationType: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
            nameType: Claims.Name,
            roleType: Claims.Role);

        identity.SetClaim(Claims.Subject, await userManager.GetUserIdAsync(user));
        identity.SetClaim(Claims.Email, await userManager.GetEmailAsync(user));
        identity.SetClaim(Claims.Name, await userManager.GetUserNameAsync(user));
        identity.SetClaim(Claims.PreferredUsername, await userManager.GetUserNameAsync(user));
        identity.SetClaim(Claims.GivenName, user.FirstName);
        identity.SetClaim(Claims.FamilyName, user.LastName);
        identity.SetClaim(HuiaConstants.ClaimTypes.Tenant, tenantId);

        foreach (var role in await userManager.GetRolesAsync(user))
        {
            identity.AddClaim(new Claim(Claims.Role, role));
        }

        identity.SetScopes(scopes);
        identity.SetDestinations(GetDestinations);

        return Results.SignIn(new ClaimsPrincipal(identity), properties: null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<IResult> ExchangeAsync(
        HttpContext context,
        IHuiaFlowIdentityFactory flowIdentity,
        UserManager<HuiaUser> userManager,
        IMultiTenantContextAccessor tenantAccessor,
        IHuiaEventPublisher events,
        TimeProvider timeProvider)
    {
        var request = context.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenIddict server request could not be retrieved.");

        if (request.IsClientCredentialsGrantType())
        {
            var identity = new ClaimsIdentity(
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, Claims.Name, Claims.Role);
            identity.SetClaim(Claims.Subject, request.ClientId!);
            identity.SetClaim(HuiaConstants.ClaimTypes.Tenant, tenantAccessor.CurrentTenantId());

            var machinePrincipal = new ClaimsPrincipal(identity);
            machinePrincipal.SetScopes(request.GetScopes());
            machinePrincipal.SetDestinations(GetDestinations);

            return Results.SignIn(machinePrincipal, properties: null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType() || request.IsDeviceCodeGrantType())
        {
            var authenticate = await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            if (authenticate.Principal is null)
            {
                return InvalidGrant("The authorization is no longer valid.");
            }

            var subject = authenticate.Principal.GetClaim(Claims.Subject);
            var user = subject is null ? null : await userManager.FindByIdAsync(subject);
            if (user is null)
            {
                return InvalidGrant("The account no longer exists.");
            }

            // Re-check the account against its flow's rules: the phone flow's IdentityOptions gate on a
            // confirmed phone number (never an email), the password flow's on a confirmed account when
            // the tenant asks for one. Lockout is checked for both.
            var isSmsSignIn = authenticate.Principal
                .GetClaims(HuiaConstants.ClaimTypes.AuthenticationMethod)
                .Contains(HuiaConstants.AuthenticationMethods.Sms, StringComparer.Ordinal);

            var flow = flowIdentity.Create(isSmsSignIn ? HuiaAuthFlow.PhoneLogin : HuiaAuthFlow.EmailAndPasswordLogin);

            if (await flow.UserManager.IsLockedOutAsync(user))
            {
                return InvalidGrant("The account is locked.");
            }

            if (!await flow.SignInManager.CanSignInAsync(user))
            {
                return InvalidGrant("The account is no longer allowed to sign in.");
            }

            var identity = new ClaimsIdentity(authenticate.Principal.Claims,
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, Claims.Name, Claims.Role);
            identity.SetDestinations(GetDestinations);

            if (request.IsAuthorizationCodeGrantType() || request.IsDeviceCodeGrantType())
            {
                await events.PublishAsync(new UserLoggedInEvent(
                    tenantAccessor.RequireCurrentTenantId(),
                    user.Id,
                    isSmsSignIn ? HuiaConstants.AuthenticationMethods.Sms : HuiaConstants.AuthenticationMethods.Password,
                    request.ClientId,
                    timeProvider.GetUtcNow()));
            }

            return Results.SignIn(new ClaimsPrincipal(identity), properties: null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        return InvalidGrant("The specified grant type is not supported.");
    }

    private static async Task<IResult> UserInfoAsync(
        HttpContext context,
        UserManager<HuiaUser> userManager)
    {
        var result = await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var principal = result.Principal;
        if (principal is null)
        {
            return Results.Challenge(
                authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
                properties: new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidToken,
                }));
        }

        var subject = principal.GetClaim(Claims.Subject);
        var claims = new Dictionary<string, object?> { [Claims.Subject] = subject };

        var user = subject is null ? null : await userManager.FindByIdAsync(subject);
        if (user is not null)
        {
            if (principal.HasScope(Scopes.Email))
            {
                claims[Claims.Email] = user.Email;
                claims[Claims.EmailVerified] = user.EmailConfirmed;
            }

            if (principal.HasScope(Scopes.Profile))
            {
                claims[Claims.Name] = user.UserName;
                claims[Claims.PreferredUsername] = user.UserName;
                claims[Claims.GivenName] = user.FirstName;
                claims[Claims.FamilyName] = user.LastName;
            }

            if (principal.HasScope(Scopes.Phone) && !string.IsNullOrEmpty(user.PhoneNumber))
            {
                claims[Claims.PhoneNumber] = user.PhoneNumber;
                claims[Claims.PhoneNumberVerified] = user.PhoneNumberConfirmed;
            }
        }

        return Results.Json(claims);
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext context,
        SignInManager<HuiaUser> signInManager,
        IMultiTenantContextAccessor tenantAccessor,
        IOpenIddictApplicationManager applicationManager,
        HuiaOptions huiaOptions)
    {
        var request = context.GetOpenIddictServerRequest();

        // Read the federation markers off the cookie before it is cleared.
        var externalIdp = context.User.FindFirstValue(HuiaOpenIdConstants.ClaimTypes.ExternalIdp);
        var externalIdToken = context.User.FindFirstValue(HuiaOpenIdConstants.ClaimTypes.ExternalIdToken);

        await signInManager.SignOutAsync();

        if (externalIdp is not null)
        {
            // The session was federated: end the upstream provider's session too, then land the browser
            // wherever the relying party wanted (resolved here since we bypass the server end-session).
            var finalRedirect = await ResolveFinalLogoutRedirectAsync(context, tenantAccessor, applicationManager, huiaOptions, request);

            var properties = new AuthenticationProperties { RedirectUri = finalRedirect };
            properties.Items[OpenIddictClientAspNetCoreConstants.Properties.RegistrationId] = externalIdp;
            if (externalIdToken is not null)
            {
                properties.Items[OpenIddictClientAspNetCoreConstants.Properties.IdentityTokenHint] = externalIdToken;
            }

            return Results.SignOut(properties, [OpenIddictClientAspNetCoreDefaults.AuthenticationScheme]);
        }

        var fallback = await ResolveLogoutFallbackAsync(context, tenantAccessor, applicationManager, huiaOptions, request);

        return Results.SignOut(
            new AuthenticationProperties { RedirectUri = fallback },
            [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
    }

    /// <summary>
    /// The absolute URL to land on once sign-out finishes. Honours the relying party's registered
    /// <c>post_logout_redirect_uri</c> when present, otherwise the client-home fallback chain.
    /// </summary>
    private static async Task<string> ResolveFinalLogoutRedirectAsync(
        HttpContext context,
        IMultiTenantContextAccessor tenantAccessor,
        IOpenIddictApplicationManager applicationManager,
        HuiaOptions huiaOptions,
        OpenIddictRequest? request)
    {
        var postLogout = request?.PostLogoutRedirectUri;
        if (!string.IsNullOrEmpty(postLogout) && request?.ClientId is { } clientId
            && await applicationManager.FindByClientIdAsync(clientId) is { } application)
        {
            var registered = await applicationManager.GetPostLogoutRedirectUrisAsync(application);
            if (registered.Contains(postLogout, StringComparer.OrdinalIgnoreCase))
            {
                return postLogout;
            }
        }

        return await ResolveLogoutFallbackAsync(context, tenantAccessor, applicationManager, huiaOptions, request);
    }

    private static async Task<string> ResolveLogoutFallbackAsync(
        HttpContext context,
        IMultiTenantContextAccessor tenantAccessor,
        IOpenIddictApplicationManager applicationManager,
        HuiaOptions huiaOptions,
        OpenIddictRequest? request)
    {
        var clientId = request?.ClientId;
        if (clientId is not null && await applicationManager.FindByClientIdAsync(clientId) is { } application)
        {
            var properties = await applicationManager.GetPropertiesAsync(application);
            if (properties.TryGetValue(HuiaOpenIdConstants.ApplicationProperties.HomeUris, out var homeUris) &&
                homeUris.ValueKind == System.Text.Json.JsonValueKind.Array &&
                homeUris.GetArrayLength() > 0)
            {
                return homeUris[0].GetString() ?? "/";
            }
        }

        var tenantId = tenantAccessor.CurrentTenantId();

        // No client identified (for example an RP that sent an empty id_token_hint): fall back to a
        // configured client's home / post-logout URL for this tenant rather than the identity server's
        // own tenant root, which has nothing to serve and would just bounce to sign-in.
        if (tenantId is not null && huiaOptions.Tenants.TryGetValue(tenantId, out var tenant)
            && TenantClientHome.Resolve(tenant) is { } appUri)
        {
            return appUri;
        }

        if (tenantId is not null)
        {
            return $"{context.Request.PathBase}/";
        }

        return "/";
    }

    private static IResult InvalidGrant(string description) => Results.Forbid(
        authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
        properties: new AuthenticationProperties(new Dictionary<string, string?>
        {
            [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
        }));

    private static IEnumerable<string> GetDestinations(Claim claim)
    {
        switch (claim.Type)
        {
            case Claims.Name or Claims.PreferredUsername:
                yield return Destinations.AccessToken;
                if (claim.Subject?.HasScope(Scopes.Profile) == true)
                {
                    yield return Destinations.IdentityToken;
                }

                yield break;

            case Claims.Email:
                yield return Destinations.AccessToken;
                if (claim.Subject?.HasScope(Scopes.Email) == true)
                {
                    yield return Destinations.IdentityToken;
                }

                yield break;

            case Claims.GivenName or Claims.FamilyName:
                yield return Destinations.AccessToken;
                if (claim.Subject?.HasScope(Scopes.Profile) == true)
                {
                    yield return Destinations.IdentityToken;
                }

                yield break;

            case HuiaConstants.ClaimTypes.AuthenticationMethod:
                yield return Destinations.AccessToken;
                yield return Destinations.IdentityToken;
                yield break;

            case Claims.Role:
                yield return Destinations.AccessToken;
                if (claim.Subject?.HasScope(Scopes.Roles) == true)
                {
                    yield return Destinations.IdentityToken;
                }

                yield break;

            case HuiaConstants.ClaimTypes.Tenant:
                yield return Destinations.AccessToken;
                yield return Destinations.IdentityToken;
                yield break;

            case "AspNet.Identity.SecurityStamp":
                yield break;

            default:
                yield return Destinations.AccessToken;
                yield break;
        }
    }
}
