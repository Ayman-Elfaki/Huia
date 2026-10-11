using Huia.Admin;
using Huia.OpenId.Entities;
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
        var inputs = body.Claims?.Select(c => new HuiaClaimInput(c.Type, c.Value));
        if (!HuiaUserAdministration.TryNormalizeClaims(body.Type, body.Value, inputs, out var claims, out var problem))
        {
            return ToProblem(problem);
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

            var outcome = await HuiaUserAdministration.AddClaimsAsync(userManager, user, claims);
            if (!outcome.IsSuccess)
            {
                return ToProblem(outcome);
            }

            await PublishUserUpdatedAsync(outcome, services, tenantId, user.Id);
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

            var outcome = await HuiaUserAdministration.RemoveClaimsAsync(userManager, user, type, value);
            if (!outcome.IsSuccess)
            {
                return ToProblem(outcome);
            }

            await PublishUserUpdatedAsync(outcome, services, tenantId, user.Id);
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
