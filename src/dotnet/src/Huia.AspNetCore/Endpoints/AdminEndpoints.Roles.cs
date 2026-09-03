using System.Text.RegularExpressions;
using Huia.EntityFrameworkCore;
using Huia.EntityFrameworkCore.Entities;
using Huia.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MR.AspNetCore.Pagination;

namespace Huia.AspNetCore.Endpoints;

/// <summary>Per-tenant roles: CRUD plus assignment to users. See <see cref="AdminEndpoints"/>.</summary>
internal static partial class AdminEndpoints
{
    [GeneratedRegex("^[A-Za-z0-9._:-]{1,256}$")]
    private static partial Regex RoleNamePattern();

    private static async Task<IResult> ListRolesAsync(HttpContext context, HuiaDbContext db, IPaginationService pagination)
    {
        var tenant = context.Request.Query["tenant"].ToString();

        var source = db.Set<HuiaRole>().IgnoreQueryFilters().AsNoTracking();
        if (!string.IsNullOrEmpty(tenant))
        {
            source = source.Where(r => r.TenantId == tenant);
        }

        var result = await pagination.KeysetPaginateAsync(
            source,
            builder => builder.Ascending(r => r.Id),
            async id => await db.Set<HuiaRole>().IgnoreQueryFilters().FirstOrDefaultAsync(r => r.Id == id, context.RequestAborted),
            roles => roles.Select(r => new RoleDto(r.Id, r.TenantId, r.Name)),
            ReadQuery(context));

        return Results.Ok(result);
    }

    private static async Task<IResult> GetRoleAsync(HttpContext context, HuiaDbContext db, string id)
    {
        var role = await db.Set<HuiaRole>().IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, context.RequestAborted);
        if (role is null)
        {
            return Results.NotFound();
        }

        var members = await db.Set<IdentityUserRole<string>>().IgnoreQueryFilters().AsNoTracking()
            .CountAsync(ur => ur.RoleId == id, context.RequestAborted);

        return Results.Ok(new RoleDetailDto(role.Id, role.TenantId, role.Name, members));
    }

    private static async Task<IResult> CreateRoleAsync(HttpContext context, HuiaOptions options, CreateRoleRequest body)
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

            var role = new HuiaRole(body.Name) { TenantId = body.Tenant };
            var result = await roleManager.CreateAsync(role);
            return result.Succeeded
                ? Results.Created($"/admin/roles/{Uri.EscapeDataString(role.Id)}", new RoleDto(role.Id, role.TenantId, role.Name))
                : IdentityProblem(result);
        });
    }

    private static async Task<IResult> UpdateRoleAsync(HttpContext context, HuiaDbContext db, string id, UpdateRoleRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Name) || !RoleNamePattern().IsMatch(body.Name))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Invalid role name."] });
        }

        var tenantId = await db.Set<HuiaRole>().IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.Id == id).Select(r => r.TenantId).FirstOrDefaultAsync(context.RequestAborted);
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

            var result = await roleManager.SetRoleNameAsync(role, body.Name);
            if (result.Succeeded)
            {
                result = await roleManager.UpdateAsync(role);
            }

            return result.Succeeded ? Results.NoContent() : IdentityProblem(result);
        });
    }

    private static async Task<IResult> DeleteRoleAsync(HttpContext context, HuiaDbContext db, string id)
    {
        var tenantId = await db.Set<HuiaRole>().IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.Id == id).Select(r => r.TenantId).FirstOrDefaultAsync(context.RequestAborted);
        if (tenantId is null)
        {
            return Results.NotFound();
        }

        if (await db.Set<IdentityUserRole<string>>().IgnoreQueryFilters().AsNoTracking().AnyAsync(ur => ur.RoleId == id, context.RequestAborted))
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

    private static async Task<IResult> GetUserRolesAsync(HttpContext context, HuiaDbContext db, string id)
    {
        var tenantId = await db.Set<HuiaUser>().IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Id == id).Select(u => u.TenantId).FirstOrDefaultAsync(context.RequestAborted);
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

    private static async Task<IResult> AddUserRoleAsync(HttpContext context, HuiaDbContext db, string id, AddUserRoleRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Role))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["role"] = ["Is required."] });
        }

        var tenantId = await db.Set<HuiaUser>().IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Id == id).Select(u => u.TenantId).FirstOrDefaultAsync(context.RequestAborted);
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
            return result.Succeeded ? Results.NoContent() : IdentityProblem(result);
        });
    }

    private static async Task<IResult> RemoveUserRoleAsync(HttpContext context, HuiaDbContext db, string id, string role)
    {
        var tenantId = await db.Set<HuiaUser>().IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Id == id).Select(u => u.TenantId).FirstOrDefaultAsync(context.RequestAborted);
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

            var result = await userManager.RemoveFromRoleAsync(user, role);
            return result.Succeeded ? Results.NoContent() : IdentityProblem(result);
        });
    }

    private sealed record RoleDto(string Id, string TenantId, string? Name);

    private sealed record RoleDetailDto(string Id, string TenantId, string? Name, int MemberCount);

    private sealed record CreateRoleRequest(string Tenant, string Name);

    private sealed record UpdateRoleRequest(string Name);

    private sealed record AddUserRoleRequest(string Role);
}
