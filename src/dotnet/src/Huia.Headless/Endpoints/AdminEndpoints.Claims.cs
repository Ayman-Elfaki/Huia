using Huia.Admin;
using Huia.Events;
using Huia.Headless.Identity;
using Huia.Multitenancy;
using Microsoft.AspNetCore.Http;

namespace Huia.Headless.Endpoints;

public static partial class AdminEndpoints
{
    private static async Task<IResult> GetUserClaimsAsync(
        HttpContext context,
        HuiaUserManager userManager,
        string id)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            return Results.NotFound();
        }

        var claims = await userManager.GetClaimsAsync(user);
        return Results.Ok(claims.Select(c => new HeadlessClaimDto(c.Type, c.Value)).ToArray());
    }

    private static async Task<IResult> AddUserClaimsAsync(
        HttpContext context,
        HuiaUserManager userManager,
        IHuiaTenantContext tenantContext,
        IHuiaEventPublisher events,
        TimeProvider timeProvider,
        string id,
        AddHeadlessUserClaimRequest body)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            return Results.NotFound();
        }

        var inputs = body.Claims?.Select(c => new HuiaClaimInput(c.Type, c.Value));
        if (!HuiaUserAdministration.TryNormalizeClaims(body.Type, body.Value, inputs, out var claims, out var problem))
        {
            return ToProblem(problem);
        }

        var outcome = await HuiaUserAdministration.AddClaimsAsync(userManager, user, claims);
        if (!outcome.IsSuccess)
        {
            return ToProblem(outcome);
        }

        await PublishUserUpdatedAsync(outcome, events, tenantContext, user.Id, timeProvider);
        return Results.Ok();
    }

    private static async Task<IResult> RemoveUserClaimAsync(
        HttpContext context,
        HuiaUserManager userManager,
        IHuiaTenantContext tenantContext,
        IHuiaEventPublisher events,
        TimeProvider timeProvider,
        string id,
        string type,
        string? value = null)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            return Results.NotFound();
        }

        var outcome = await HuiaUserAdministration.RemoveClaimsAsync(userManager, user, type, value);
        if (!outcome.IsSuccess)
        {
            return ToProblem(outcome);
        }

        await PublishUserUpdatedAsync(outcome, events, tenantContext, user.Id, timeProvider);
        return Results.NoContent();
    }

    private static async Task<IResult> RemoveUserClaimsByQueryAsync(
        HttpContext context,
        HuiaUserManager userManager,
        IHuiaTenantContext tenantContext,
        IHuiaEventPublisher events,
        TimeProvider timeProvider,
        string id,
        string? type = null,
        string? value = null)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["type"] = ["The 'type' query parameter is required."],
            });
        }

        return await RemoveUserClaimAsync(context, userManager, tenantContext, events, timeProvider, id, type, value);
    }
}

/// <summary>Represents a claim in the headless admin API.</summary>
public sealed record HeadlessClaimDto(string Type, string Value);

/// <summary>Request body for adding claims to a user in the headless admin API.</summary>
public sealed record AddHeadlessUserClaimRequest(
    string? Type = null,
    string? Value = null,
    HeadlessClaimDto[]? Claims = null);
