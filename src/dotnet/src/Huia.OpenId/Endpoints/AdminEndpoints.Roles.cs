using System.Text.RegularExpressions;
using Huia.Events;
using Huia.OpenId.EntityFrameworkCore.Entities;
using Huia.Options;
using Huia.Stores;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.OpenId.Endpoints;

/// <summary>Per-tenant roles: CRUD plus assignment to users. See <see cref="AdminEndpoints"/>.</summary>
internal static partial class AdminEndpoints
{
    [GeneratedRegex("^[A-Za-z0-9._:-]{1,256}$")]
    private static partial Regex RoleNamePattern();

    private static async Task<IResult> ListRolesAsync(
        HttpContext context,
        IHuiaOpenIdAdminStore<HuiaUser, HuiaRole> store,
        string? tenant = null,
        string? after = null,
        string? before = null,
        int? size = 25)
    {
        var query = new HuiaRoleQuery
        {
            TenantId = string.IsNullOrEmpty(tenant) ? null : tenant,
            PageSize = Math.Clamp(size ?? 25, 1, 100),
            After = after,
            Before = before,
        };

        var result = await store.ListRolesAsync(query, context.RequestAborted);

        return Results.Ok(new
        {
            data = result.Data.Select(r => new RoleDto(r.Id, r.TenantId, r.Name, r.Origin)),
            result.HasNext,
            result.HasPrevious,
            result.NextCursor,
            result.PreviousCursor,
        });
    }

    private static async Task<IResult> GetRoleAsync(
        HttpContext context,
        IHuiaOpenIdAdminStore<HuiaUser, HuiaRole> store,
        string id)
    {
        var (tenantId, _) = await store.GetRoleMetadataAsync(id, context.RequestAborted);
        if (tenantId is null)
        {
            return Results.NotFound();
        }

        return await WithTenantScopeAsync(context, tenantId, async services =>
        {
            var roleManager = services.GetRequiredService<RoleManager<HuiaRole>>();
            var role = await roleManager.FindByIdAsync(id);
            if (role is null)
            {
                return Results.NotFound();
            }

            var members = await store.GetRoleMemberCountAsync(id, context.RequestAborted);
            return Results.Ok(new RoleDetailDto(role.Id, role.TenantId, role.Name, role.Origin, members));
        });
    }

    private static async Task<IResult> CreateRoleAsync(
        HttpContext context,
        HuiaOptions options,
        CreateRoleRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Tenant) || !options.Tenants.ContainsKey(body.Tenant))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["tenant"] = ["Unknown tenant."] });
        }

        if (string.IsNullOrWhiteSpace(body.Name) || !RoleNamePattern().IsMatch(body.Name))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["name"] = ["A role name must be 1-256 characters of letters, digits, '.', '_', ':' or '-'."],
            });
        }

        return await WithTenantScopeAsync(context, body.Tenant, async services =>
        {
            var roleManager = services.GetRequiredService<RoleManager<HuiaRole>>();
            if (await roleManager.RoleExistsAsync(body.Name))
            {
                return Results.Conflict(new { message = $"Role '{body.Name}' already exists in tenant '{body.Tenant}'." });
            }

            var role = new HuiaRole(body.Name) { TenantId = body.Tenant, Origin = HuiaConstants.Origins.Dynamic };
            var result = await roleManager.CreateAsync(role);
            return result.Succeeded
                ? Results.Created($"/admin/roles/{Uri.EscapeDataString(role.Id)}",
                    new RoleDto(role.Id, role.TenantId, role.Name, role.Origin))
                : IdentityProblem(result);
        });
    }

    private static async Task<IResult> UpdateRoleAsync(
        HttpContext context,
        IHuiaOpenIdAdminStore<HuiaUser, HuiaRole> store,
        string id,
        UpdateRoleRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Name) || !RoleNamePattern().IsMatch(body.Name))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Invalid role name."] });
        }

        var (tenantId, _) = await store.GetRoleMetadataAsync(id, context.RequestAborted);
        if (tenantId is null)
        {
            return Results.NotFound();
        }

        return await WithTenantScopeAsync(context, tenantId, async services =>
        {
            var roleManager = services.GetRequiredService<RoleManager<HuiaRole>>();
            var role = await roleManager.FindByIdAsync(id);
            if (role is null)
            {
                return Results.NotFound();
            }

            if (role.Origin == HuiaConstants.Origins.Static)
            {
                return CodeDefinedRoleProblem();
            }

            var result = await roleManager.SetRoleNameAsync(role, body.Name);
            if (result.Succeeded)
            {
                result = await roleManager.UpdateAsync(role);
            }

            return result.Succeeded ? Results.NoContent() : IdentityProblem(result);
        });
    }

    private static async Task<IResult> DeleteRoleAsync(
        HttpContext context,
        IHuiaOpenIdAdminStore<HuiaUser, HuiaRole> store,
        string id)
    {
        var (tenantId, origin) = await store.GetRoleMetadataAsync(id, context.RequestAborted);
        if (tenantId is null)
        {
            return Results.NotFound();
        }

        if (origin == HuiaConstants.Origins.Static)
        {
            return CodeDefinedRoleProblem();
        }

        if (await store.IsRoleAssignedToAnyUserAsync(id, context.RequestAborted))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "This role still has members. Unassign them before deleting it.");
        }

        return await WithTenantScopeAsync(context, tenantId, async services =>
        {
            var roleManager = services.GetRequiredService<RoleManager<HuiaRole>>();
            var role = await roleManager.FindByIdAsync(id);
            if (role is null)
            {
                return Results.NotFound();
            }

            var result = await roleManager.DeleteAsync(role);
            return result.Succeeded ? Results.NoContent() : IdentityProblem(result);
        });
    }

    private static async Task<IResult> GetUserRolesAsync(
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
            return user is null ? Results.NotFound() : Results.Ok((await userManager.GetRolesAsync(user)).ToArray());
        });
    }

    private static async Task<IResult> AddUserRoleAsync(
        HttpContext context,
        IHuiaOpenIdAdminStore<HuiaUser, HuiaRole> store,
        string id,
        AddUserRoleRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Role))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["role"] = ["Is required."] });
        }

        var tenantId = await store.GetUserTenantIdAsync(id, context.RequestAborted);
        if (tenantId is null)
        {
            return Results.NotFound();
        }

        return await WithTenantScopeAsync(context, tenantId, async services =>
        {
            var userManager = services.GetRequiredService<UserManager<HuiaUser>>();
            var roleManager = services.GetRequiredService<RoleManager<HuiaRole>>();

            var user = await userManager.FindByIdAsync(id);
            if (user is null)
            {
                return Results.NotFound();
            }

            if (!await roleManager.RoleExistsAsync(body.Role))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["role"] = ["Unknown role."] });
            }

            if (await userManager.IsInRoleAsync(user, body.Role))
            {
                return Results.NoContent();
            }

            var result = await userManager.AddToRoleAsync(user, body.Role);
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

    private static async Task<IResult> RemoveUserRoleAsync(
        HttpContext context,
        IHuiaOpenIdAdminStore<HuiaUser, HuiaRole> store,
        string id,
        string role)
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

            if (!await userManager.IsInRoleAsync(user, role))
            {
                return Results.NoContent();
            }

            var result = await userManager.RemoveFromRoleAsync(user, role);
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

    private static IResult CodeDefinedRoleProblem()
        => Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "This role is defined in code and cannot be modified through the admin API.");

    private sealed record RoleDto(string Id, string TenantId, string? Name, string Origin);

    private sealed record RoleDetailDto(string Id, string TenantId, string? Name, string Origin, int MemberCount);

    private sealed record CreateRoleRequest(string Tenant, string Name);

    private sealed record UpdateRoleRequest(string Name);

    private sealed record AddUserRoleRequest(string Role);
}
