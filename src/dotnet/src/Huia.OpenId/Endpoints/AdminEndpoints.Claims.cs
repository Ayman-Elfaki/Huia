using System.Security.Claims;
using Huia.Events;
using Huia.OpenId.EntityFrameworkCore.Entities;
using Huia.Stores;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.OpenId.Endpoints;

/// <summary>Claims management for users. See <see cref="AdminEndpoints"/>.</summary>
internal static partial class AdminEndpoints
{
    private static async Task<IResult> GetUserClaimsAsync(
        HttpContext context,
        IHuiaOpenIdAdminStore<HuiaUser, HuiaRole> store,
        string id)
    {
        var tenantId = await store.GetUserTenantIdAsync(id, context.RequestAborted);
        if (tenantId is null)
        {
            return Results.NotFound();
        }

        return await WithTenantScopeAsync(context, tenantId, async services =>
        {
            var userManager = services.GetRequiredService<UserManager<HuiaUser>>();
            var user = await userManager.FindByIdAsync(id);
            if (user is null)
            {
                return Results.NotFound();
            }

            var claims = await userManager.GetClaimsAsync(user);
            return Results.Ok(claims.Select(c => new ClaimDto(c.Type, c.Value)).ToArray());
        });
    }

    private static async Task<IResult> AddUserClaimsAsync(
        HttpContext context,
        IHuiaOpenIdAdminStore<HuiaUser, HuiaRole> store,
        string id,
        AddUserClaimRequest body)
    {
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

        var tenantId = await store.GetUserTenantIdAsync(id, context.RequestAborted);
        if (tenantId is null)
        {
            return Results.NotFound();
        }

        return await WithTenantScopeAsync(context, tenantId, async services =>
        {
            var userManager = services.GetRequiredService<UserManager<HuiaUser>>();
            var user = await userManager.FindByIdAsync(id);
            if (user is null)
            {
                return Results.NotFound();
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
                var events = services.GetRequiredService<IHuiaEventPublisher>();
                var timeProvider = services.GetService<TimeProvider>() ?? TimeProvider.System;
                await events.PublishAsync(new UserUpdatedEvent(tenantId, user.Id, timeProvider.GetUtcNow()));
            }

            return Results.NoContent();
        });
    }

    private static async Task<IResult> RemoveUserClaimAsync(
        HttpContext context,
        IHuiaOpenIdAdminStore<HuiaUser, HuiaRole> store,
        string id,
        string type,
        string? value = null)
    {
        var tenantId = await store.GetUserTenantIdAsync(id, context.RequestAborted);
        if (tenantId is null)
        {
            return Results.NotFound();
        }

        return await WithTenantScopeAsync(context, tenantId, async services =>
        {
            var userManager = services.GetRequiredService<UserManager<HuiaUser>>();
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

            var events = services.GetRequiredService<IHuiaEventPublisher>();
            var timeProvider = services.GetService<TimeProvider>() ?? TimeProvider.System;
            await events.PublishAsync(new UserUpdatedEvent(tenantId, user.Id, timeProvider.GetUtcNow()));
            return Results.NoContent();
        });
    }

    private static async Task<IResult> RemoveUserClaimsByQueryAsync(
        HttpContext context,
        IHuiaOpenIdAdminStore<HuiaUser, HuiaRole> store,
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

        return await RemoveUserClaimAsync(context, store, id, type, value);
    }

    private sealed record ClaimDto(string Type, string Value);

    private sealed record AddUserClaimRequest(
        string? Type = null,
        string? Value = null,
        ClaimDto[]? Claims = null);
}
