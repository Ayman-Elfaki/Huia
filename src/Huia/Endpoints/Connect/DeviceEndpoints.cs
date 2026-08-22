using System.Security.Claims;
using Huia.Common;
using Huia.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;

namespace Huia.Endpoints.Connect;


/// <summary>
/// The JSON half of the device authorization flow's approval step — the bundled <c>Device</c> Razor Page
/// (<c>Areas/Identity/Pages/Account/Device.cshtml</c>) is what <c>verification_uri</c> actually points a real
/// user's browser at by default (see <see cref="DeviceVerifier"/>'s own doc comment); this stays
/// mapped as a secondary recognized verification URI for a client that wants to drive its own UI against the
/// raw JSON contract instead, exposed as JSON on <c>GET</c> but form-encoded on <c>POST</c>: OpenIddict's own
/// end-user-verification passthrough handler validates every request against this route the same way it does
/// <c>/connect/token</c> — rejecting any <c>Content-Type</c> other than <c>application/x-www-form-urlencoded</c>
/// before this endpoint's own handler ever runs — so the decision has to travel as a form field, not a JSON
/// body. <c>[FromForm]</c> binding makes ASP.NET Core require antiforgery validation on the POST
/// automatically — appropriately, since a bare authenticated-cookie POST here would otherwise let any page
/// CSRF a signed-in user into approving a device code the attacker generated for themselves ("device code
/// phishing"). The token to satisfy that is handed out by the GET above (<c>antiforgeryToken</c>) rather than
/// a separate endpoint, since the client already has to call GET first to learn what it's being asked to
/// approve. The consuming app still needs <c>app.UseAntiforgery()</c> (after
/// <c>UseAuthentication</c>/<c>UseAuthorization</c>) for this to work — see docs/getting-started.md.
/// </summary>
internal static class DeviceEndpoints
{
    public static IEndpointRouteBuilder MapDeviceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("verify")
            .RequireAuthorization(policy => policy
                .AddAuthenticationSchemes(HuiaAuthenticationDefaults.ApplicationScheme)
                .RequireAuthenticatedUser());

        group.MapGet("", HandleGetAsync);
        group.MapPost("", HandlePostAsync);

        return endpoints;
    }

    private static async Task<IResult> HandleGetAsync(HttpContext httpContext,
        IOpenIddictApplicationManager applicationManager, IAntiforgery antiforgery)
    {
        var result = await DeviceVerifier.AuthenticateAsync(httpContext).ConfigureAwait(false);
        var request = await DeviceVerifier.TryDescribeAsync(result, applicationManager).ConfigureAwait(false);
        if (request is null)
        {
            return InvalidCode();
        }

        // Issued alongside the antiforgery cookie set on this same response; the approving client echoes it
        // back as the "__RequestVerificationToken" form field on its POST (ASP.NET Core's default field name
        // for a request token, so no extra client-side configuration is needed to send it correctly).
        var antiforgeryToken = antiforgery.GetAndStoreTokens(httpContext).RequestToken;

        return Results.Ok(new
        {
            applicationName = request.ApplicationName,
            scope = string.Join(" ", request.Scopes),
            userCode = request.UserCode,
            antiforgeryToken,
        });
    }

    private static async Task<IResult> HandlePostAsync([FromForm] DeviceVerificationDecision decision,
        HttpContext httpContext, HuiaUserManager userManager, IOpenIddictScopeManager scopeManager,
        IOpenIddictApplicationManager applicationManager)
    {
        var result = await DeviceVerifier.AuthenticateAsync(httpContext).ConfigureAwait(false);
        var request = await DeviceVerifier.TryDescribeAsync(result, applicationManager).ConfigureAwait(false);
        if (request is null)
        {
            return InvalidCode();
        }

        if (!decision.Accept)
        {
            return Results.Forbid(authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
        }

        var user = await userManager.GetUserAsync(httpContext.User).ConfigureAwait(false);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var identity = await DeviceVerifier
            .ApproveAsync(user, request.Scopes, userManager, scopeManager)
            .ConfigureAwait(false);

        return Results.SignIn(new ClaimsPrincipal(identity), properties: null,
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static IResult InvalidCode() => Results.BadRequest(new
    {
        error = OpenIddictConstants.Errors.InvalidToken, errorDescription = "The specified user code is not valid or has expired."
    });

    private sealed record DeviceVerificationDecision(bool Accept);
}
