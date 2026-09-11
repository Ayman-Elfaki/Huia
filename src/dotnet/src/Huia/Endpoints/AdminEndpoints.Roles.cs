using System.Text.RegularExpressions;
using Huia.Identity;
using Huia.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.Endpoints;

/// <summary>Per-tenant roles: CRUD plus assignment to users. See <see cref="AdminEndpoints"/>.</summary>
internal static partial class AdminEndpoints
{
    [GeneratedRegex("^[A-Za-z0-9._:-]{1,256}$")]
    private static partial Regex RoleNamePattern();

    private static async Task<IResult> ListRolesAsync(HttpContext context, IHuiaAdminStore adminStore)
    {
        var tenant = context.Request.Query["tenant"].ToString();
        var result = await adminStore.ListRolesAsync(
            string.IsNullOrEmpty(tenant) ? null : tenant,
            ReadQuery(context),
            context.RequestAborted);

        return Results.Ok(result);
    }

    private static async Task<IResult> GetRoleAsync(IHuiaAdminStore adminStore, string id, CancellationToken ct)
    {
        var detail = await adminStore.GetRoleDetailAsync(id, ct);
        return detail is null ? Results.NotFound() : Results.Ok(detail);
    }

    private static async Task<IResult> CreateRoleAsync(HttpContext context, HuiaOptions options, CreateRoleRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Tenant) || !options.Tenants.ContainsKey(body.Tenant))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["tenant"] = ["Unknown tenant."] });
        }

        if (string.IsNullOrWhiteSpace(body.Name) || !RoleNamePattern().IsMatch(body.Name))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Invalid role name."] });
        }

        return await WithTenantScopeAsync(context, body.Tenant, async services =>
        {
            var roleManager = services.GetRequiredService<RoleManager<HuiaRole>>();
            if (await roleManager.RoleExistsAsync(body.Name))
            {
                return Results.Conflict(new { message = $"A role '{body.Name}' already exists in tenant '{body.Tenant}'." });
            }

            var role = new HuiaRole(body.Name)
            {
                TenantId = body.Tenant,
                Origin = HuiaConstants.Origins.Dynamic,
            };

            var result = await roleManager.CreateAsync(role);
            return result.Succeeded
                ? Results.Created($"/admin/roles/{Uri.EscapeDataString(role.Id)}",
                    new RoleDto(role.Id, role.TenantId, role.Name, role.Origin))
                : IdentityProblem(result);
        });
    }

    private static async Task<IResult> UpdateRoleAsync(
        HttpContext context, IHuiaAdminStore adminStore, string id, UpdateRoleRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Name) || !RoleNamePattern().IsMatch(body.Name))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Invalid role name."] });
        }

        var tenantId = await adminStore.GetRoleTenantIdAsync(id, context.RequestAborted);
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
        HttpContext context, IHuiaAdminStore adminStore, string id)
    {
        var tenantId = await adminStore.GetRoleTenantIdAsync(id, context.RequestAborted);
        if (tenantId is null)
        {
            return Results.NotFound();
        }

        var detail = await adminStore.GetRoleDetailAsync(id, context.RequestAborted);
        if (detail?.Origin == HuiaConstants.Origins.Static)
        {
            return CodeDefinedRoleProblem();
        }

        if (await adminStore.HasRoleMembersAsync(id, context.RequestAborted))
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

    private static IResult CodeDefinedRoleProblem()
        => Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "This role is defined in code and cannot be modified through the admin API.");

    private sealed record CreateRoleRequest(string Tenant, string Name);

    private sealed record UpdateRoleRequest(string Name);
}
