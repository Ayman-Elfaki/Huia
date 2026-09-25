using System.Security.Claims;
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

        var claimsToAdd = new List<(string Type, string Value)>();
        if (!string.IsNullOrWhiteSpace(body.Type))
        {
            claimsToAdd.Add((body.Type.Trim(), body.Value?.Trim() ?? string.Empty));
        }

        if (body.Claims is { Length: > 0 })
        {
            foreach (var c in body.Claims)
            {
                if (string.IsNullOrWhiteSpace(c.Type))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["claims"] = ["Each claim must have a non-empty type."],
                    });
                }

                claimsToAdd.Add((c.Type.Trim(), c.Value?.Trim() ?? string.Empty));
            }
        }

        if (claimsToAdd.Count == 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["claims"] = ["At least one claim (type and value) must be provided."],
            });
        }

        var existingClaims = await userManager.GetClaimsAsync(user);
        var newlyAdded = false;

        foreach (var (type, value) in claimsToAdd)
        {
            if (existingClaims.Any(c => c.Type == type && c.Value == value))
            {
                continue;
            }

            var result = await userManager.AddClaimAsync(user, new Claim(type, value));
            if (!result.Succeeded)
            {
                return IdentityProblem(result);
            }

            newlyAdded = true;
        }

        if (newlyAdded)
        {
            await events.PublishAsync(new UserUpdatedEvent(tenantContext.CurrentTenantId, user.Id,
                timeProvider.GetUtcNow()));
        }

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

        var existingClaims = await userManager.GetClaimsAsync(user);
        var toRemove = existingClaims
            .Where(c => c.Type == type && (value is null || c.Value == value))
            .ToList();

        if (toRemove.Count == 0)
        {
            return Results.NoContent();
        }

        var result = await userManager.RemoveClaimsAsync(user, toRemove);
        if (!result.Succeeded)
        {
            return IdentityProblem(result);
        }

        await events.PublishAsync(
            new UserUpdatedEvent(tenantContext.CurrentTenantId, user.Id, timeProvider.GetUtcNow()));
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
