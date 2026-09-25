using Huia.Entities;
using Huia.Headless.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Huia.Headless.Endpoints;

/// <summary>
/// The bearer-protected <c>identity/me</c> endpoint: the claims shape a Huia.Headless client needs to
/// populate a session (<c>sub</c>, <c>email</c>, name, roles) — richer than <c>MapIdentityApi</c>'s stock
/// <c>/manage/info</c> (<c>email</c>/<c>isEmailConfirmed</c> only), and independent of it so a caller does
/// not have to reverse-engineer claims out of the (intentionally opaque) bearer token.
/// </summary>
internal static class MeEndpoints
{
    public static void MapHuiaHeadlessMeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("identity/me", GetMeAsync)
            .RequireAuthorization()
            .WithName(HuiaConstants.Endpoints.Headless.Me);
    }

    private static async Task<IResult> GetMeAsync(HttpContext context, HuiaUserManager userManager)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var roles = await userManager.GetRolesAsync(user);

        return Results.Ok(new MeResponse(
            user.Id,
            user.Email,
            user.EmailConfirmed,
            user.PhoneNumber,
            user.PhoneNumberConfirmed,
            user.FirstName,
            user.LastName,
            [.. roles]));
    }

    private sealed record MeResponse(
        string Sub,
        string? Email,
        bool EmailConfirmed,
        string? PhoneNumber,
        bool PhoneNumberConfirmed,
        string FirstName,
        string LastName,
        string[] Roles);
}
