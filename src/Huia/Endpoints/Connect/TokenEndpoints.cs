using System.Security.Claims;
using Huia.Common;
using Huia.Identity;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;


namespace Huia.Endpoints.Connect;

internal static class TokenEndpoints
{
    public static IEndpointRouteBuilder MapTokenEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("token", HandleAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        UserManager<HuiaUser> userManager,
        SignInManager<HuiaUser> signInManager,
        IOpenIddictApplicationManager applicationManager,
        IOpenIddictScopeManager scopeManager)
    {
        var request = httpContext.GetOpenIddictServerRequest()
                      ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        if (request.IsClientCredentialsGrantType())
        {
            return await HandleClientCredentialsAsync(request, applicationManager, scopeManager).ConfigureAwait(false);
        }

        if (request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType() ||
            request.IsDeviceCodeGrantType())
        {
            return await HandleReAuthenticateAsync(httpContext, userManager, signInManager)
                .ConfigureAwait(false);
        }

        return Forbid(Errors.UnsupportedGrantType, "The specified grant type is not supported.");
    }

    private static async Task<IResult> HandleClientCredentialsAsync(
        OpenIddictRequest request,
        IOpenIddictApplicationManager applicationManager,
        IOpenIddictScopeManager scopeManager)
    {
        var application = await applicationManager.FindByClientIdAsync(request.ClientId!).ConfigureAwait(false)
                          ?? throw new InvalidOperationException(
                              "The application details cannot be found in the database.");

        var identity = new ClaimsIdentity(authenticationType: "Huia", nameType: Claims.Name, roleType: Claims.Role);

        identity.SetClaim(Claims.Subject, await applicationManager.GetClientIdAsync(application).ConfigureAwait(false));
        identity.SetClaim(Claims.Name, await applicationManager.GetDisplayNameAsync(application).ConfigureAwait(false));

        var scopes = request.GetScopes();
        identity.SetScopes(scopes);
        identity.SetResources(await scopeManager.ListResourcesAsync(scopes).ToListAsync().ConfigureAwait(false));
        identity.SetDestinations(_ => [Destinations.AccessToken]);

        return Results.SignIn(new ClaimsPrincipal(identity), properties: null,
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Authorization-code exchange, refresh-token redemption, and device-code polling all reduce to the
    /// same operation: recover the principal OpenIddict already validated from the code/token, make sure
    /// the user can still sign in, and re-issue.
    /// </summary>
    private static async Task<IResult> HandleReAuthenticateAsync(HttpContext httpContext,
        UserManager<HuiaUser> userManager, SignInManager<HuiaUser> signInManager)
    {
        var result = await httpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)
            .ConfigureAwait(false);

        var userId = result.Principal?.GetClaim(Claims.Subject);
        var user = userId is null ? null : await userManager.FindByIdAsync(userId).ConfigureAwait(false);

        if (user is null)
        {
            return Forbid(Errors.InvalidGrant, "The token is no longer valid.");
        }

        if (!await signInManager.CanSignInAsync(user).ConfigureAwait(false))
        {
            return Forbid(Errors.InvalidGrant, "The user is no longer allowed to sign in.");
        }

        var principal = result.Principal!;

        var identity = await ClaimsUtils.CreateUserIdentityAsync(userManager, user, principal.GetScopes())
            .ConfigureAwait(false);
        identity.SetResources(principal.GetResources());

        return Results.SignIn(new ClaimsPrincipal(identity), properties: null,
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static IResult Forbid(string error, string description) => Results.Forbid(
        authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
        properties: new AuthenticationProperties(new Dictionary<string, string?>
        {
            [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
        }));
}