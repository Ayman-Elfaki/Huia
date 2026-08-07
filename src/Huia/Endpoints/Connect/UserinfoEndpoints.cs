using System.Security.Claims;
using Huia.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation.AspNetCore;

namespace Huia.Endpoints.Connect;

internal static class UserinfoEndpoints
{
    public static IEndpointRouteBuilder MapUserinfoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapMethods("userinfo", [HttpMethods.Get, HttpMethods.Post], HandleAsync)
            .RequireAuthorization(policy => policy
                .AddAuthenticationSchemes(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser());

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(ClaimsPrincipal principal, UserManager<HuiaUser> userManager)
    {
        var user = await userManager.FindByIdAsync(principal.GetClaim(OpenIddictConstants.Claims.Subject)!)
            .ConfigureAwait(false);
        if (user is null)
        {
            return Results.Forbid(
                authenticationSchemes: [OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme],
                properties: new AuthenticationProperties(new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.InvalidToken,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                        "The specified access token is bound to an account that no longer exists.",
                }));
        }

        var claims = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [OpenIddictConstants.Claims.Subject] = await userManager.GetUserIdAsync(user).ConfigureAwait(false),
        };

        if (principal.HasScope(OpenIddictConstants.Scopes.Email))
        {
            claims[OpenIddictConstants.Claims.Email] = await userManager.GetEmailAsync(user).ConfigureAwait(false);
            claims[OpenIddictConstants.Claims.EmailVerified] =
                await userManager.IsEmailConfirmedAsync(user).ConfigureAwait(false);
        }

        if (principal.HasScope(OpenIddictConstants.Scopes.Profile))
        {
            var userName = await userManager.GetUserNameAsync(user).ConfigureAwait(false);
            claims[OpenIddictConstants.Claims.Name] = userName;
            claims[OpenIddictConstants.Claims.PreferredUsername] = userName;
            claims[OpenIddictConstants.Claims.GivenName] = user.FirstName;
            claims[OpenIddictConstants.Claims.FamilyName] = user.LastName;
        }

        if (principal.HasScope(OpenIddictConstants.Scopes.Roles))
        {
            claims[OpenIddictConstants.Claims.Role] = await userManager.GetRolesAsync(user).ConfigureAwait(false);
        }

        return Results.Ok(claims);
    }
}