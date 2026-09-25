using System.ComponentModel.DataAnnotations;
using Huia.Entities;
using Huia.Events;
using Huia.Headless.Identity;
using Huia.Multitenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Huia.Headless.Endpoints;

/// <summary>
/// Replaces <c>MapIdentityApi</c>'s stock <c>POST identity/register</c>. The stock request DTO carries
/// only email and password — any first/last name in the request body would be silently discarded even
/// if the client sent one — while every other Headless signup path (<see cref="PhoneEndpoints"/>'s and
/// <see cref="ExternalEndpoints"/>'s complete-profile steps) already requires both. This endpoint is
/// mapped with a lower route order than <c>MapIdentityApi</c>'s own <c>/register</c>, so it always wins
/// the match and the stock handler is simply never reached (not an ambiguous-route error).
/// </summary>
internal static class RegisterEndpoints
{
    private static readonly EmailAddressAttribute EmailAddressValidator = new();

    public static void MapHuiaHeadlessRegisterEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("identity/register", RegisterAsync)
            .WithName(HuiaConstants.Endpoints.Headless.Register)
            .WithOrder(-1);
    }

    private static async Task<IResult> RegisterAsync(
        HuiaUserManager userManager,
        IHuiaTenantContext tenantContext,
        IHuiaEventPublisher events,
        TimeProvider timeProvider,
        RegisterRequest body)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(body.Email) || !EmailAddressValidator.IsValid(body.Email))
        {
            errors["email"] = ["A valid email address is required."];
        }

        if (string.IsNullOrWhiteSpace(body.FirstName))
        {
            errors["firstName"] = ["First name is required."];
        }

        if (string.IsNullOrWhiteSpace(body.LastName))
        {
            errors["lastName"] = ["Last name is required."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var user = new HuiaUser
        {
            UserName = body.Email,
            Email = body.Email,
            FirstName = body.FirstName,
            LastName = body.LastName,
        };

        var result = await userManager.CreateAsync(user, body.Password);
        if (!result.Succeeded)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["identity"] = result.Errors.Select(e => e.Description).ToArray(),
            });
        }

        await events.PublishAsync(new UserRegisteredEvent(
            tenantContext.CurrentTenantId,
            user.Id,
            user.UserName!,
            user.Email,
            HuiaConstants.AuthenticationMethods.Password,
            timeProvider.GetUtcNow()));

        return Results.Ok();
    }

    /// <summary>Body of <c>POST identity/register</c>.</summary>
    /// <param name="Email">The account email / username.</param>
    /// <param name="Password">The account password.</param>
    /// <param name="FirstName">The account holder's first name.</param>
    /// <param name="LastName">The account holder's last name.</param>
    public sealed record RegisterRequest(string Email, string Password, string FirstName, string LastName);
}
