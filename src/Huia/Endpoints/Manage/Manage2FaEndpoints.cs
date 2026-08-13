using System.Security.Claims;
using Huia.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;

namespace Huia.Endpoints.Manage;

/// <summary>
/// JSON endpoints for a signed-in user to manage their own account (two-factor, email, password) — for an
/// SPA/native client to build its own account-settings UI against. Registration, sign-in, and password
/// recovery are server-rendered pages instead (see <c>Huia</c>), not JSON APIs: OpenIddict's
/// <c>/connect/authorize</c> needs a real browser session (the <c>Identity.Application</c> cookie) to exist
/// before it can issue a code, so there's no JSON client that would call a login endpoint directly.
/// </summary>
internal static class Manage2FaEndpoints
{
    public static RouteGroupBuilder MapManage2FaEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("2fa");

        group.MapPost("", TwoFactorAsync);

        return group;
    }

    private static async Task<IResult> TwoFactorAsync(TwoFactorRequest request, ClaimsPrincipal principal,
        UserManager<HuiaUser> userManager)
    {
        var user = await userManager.GetSignedInUserAsync(principal).ConfigureAwait(false);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        if (request.Enable == true)
        {
            if (!string.IsNullOrEmpty(request.TwoFactorCode))
            {
                var verified = await userManager.VerifyTwoFactorTokenAsync(user,
                    userManager.Options.Tokens.AuthenticatorTokenProvider, request.TwoFactorCode).ConfigureAwait(false);
                if (!verified)
                {
                    return Results.Problem("Invalid two-factor code.", statusCode: StatusCodes.Status400BadRequest);
                }
            }

            await userManager.SetTwoFactorEnabledAsync(user, true).ConfigureAwait(false);
        }
        else if (request.Enable == false)
        {
            await userManager.SetTwoFactorEnabledAsync(user, false).ConfigureAwait(false);
        }

        string[]? recoveryCodes = null;
        if (request.ResetRecoveryCodes == true || (request.Enable == true &&
                                                   await userManager.CountRecoveryCodesAsync(user)
                                                       .ConfigureAwait(false) == 0))
        {
            recoveryCodes = (await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10).ConfigureAwait(false))
                ?.ToArray();
        }

        // Only provisions a key on an explicit ResetSharedKey request — never implicitly just because none
        // exists yet, or a bare status-check call (every field null, used to render the current state)
        // would silently generate and persist an authenticator secret nobody asked for. A caller wanting to
        // start enrollment sends ResetSharedKey explicitly to get one.
        if (request.ResetSharedKey == true)
        {
            await userManager.ResetAuthenticatorKeyAsync(user).ConfigureAwait(false);
        }

        var sharedKey = await userManager.GetAuthenticatorKeyAsync(user).ConfigureAwait(false);

        return Results.Ok(new TwoFactorResponse(
            await userManager.GetTwoFactorEnabledAsync(user).ConfigureAwait(false),
            sharedKey,
            recoveryCodes,
            recoveryCodes?.Length ?? await userManager.CountRecoveryCodesAsync(user).ConfigureAwait(false)));
    }

    private static Dictionary<string, string[]> ToErrorDictionary(IdentityResult result)
        => result.Errors
            .GroupBy(e => e.Code, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray(), StringComparer.Ordinal);

    private sealed record TwoFactorRequest(
        bool? Enable,
        string? TwoFactorCode,
        bool? ResetSharedKey,
        bool? ResetRecoveryCodes);

    private sealed record TwoFactorResponse(
        bool IsTwoFactorEnabled,
        string? SharedKey,
        string[]? RecoveryCodes,
        int RecoveryCodesLeft);
}