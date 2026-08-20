using Huia.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;

namespace Huia.Endpoints.Admin;


/// <summary>
/// JSON CRUD endpoints over <see cref="HuiaRole"/>, backed by <see cref="RoleManager{TRole}"/>.
/// </summary>
internal static class RolesEndpoints
{
    public static IEndpointRouteBuilder MapRolesEndpoints(this IEndpointRouteBuilder api)
    {
        var roles = api.MapGroup("/roles");

        roles.MapGet("", ListAsync);
        roles.MapGet("/{id}", GetAsync);
        roles.MapPost("", CreateAsync);
        roles.MapPut("/{id}", RenameAsync);
        roles.MapDelete("/{id}", DeleteAsync);
        roles.MapGet("/{id}/members", GetMembersAsync);

        return api;
    }

    private static IResult ListAsync(RoleManager<HuiaRole> roleManager)
    {
        if (!roleManager.SupportsQueryableRoles)
        {
            return Results.Problem("The configured role store doesn't support listing roles.",
                statusCode: StatusCodes.Status501NotImplemented);
        }

        var items = roleManager.Roles
            .OrderBy(r => r.Name)
            .Select(r => new RoleResponse(r.Id, r.Name!))
            .ToList();

        // Unpaginated by design (roles are typically few) - always the whole list, so there's never a next or
        // previous page. Field names mirror MR.AspNetCore.Pagination's KeysetPaginationResult<T> (used by the
        // other four admin list endpoints when an EF Core store is configured) so the admin UI's list
        // composable can treat every resource uniformly, even though this endpoint never involves that
        // package - Roles doesn't need real pagination, only the same response shape.
        return Results.Ok(new RolesPage<RoleResponse>(items, items.Count, items.Count, HasPrevious: false,
            HasNext: false));
    }

    private static async Task<IResult> GetAsync(string id, RoleManager<HuiaRole> roleManager)
    {
        var role = await roleManager.FindByIdAsync(id).ConfigureAwait(false);
        return role is null ? Results.NotFound() : Results.Ok(new RoleResponse(role.Id, role.Name!));
    }

    private static async Task<IResult> CreateAsync(CreateRoleRequest request, RoleManager<HuiaRole> roleManager)
    {
        var role = new HuiaRole { Name = request.Name };
        var result = await roleManager.CreateAsync(role).ConfigureAwait(false);

        return result.Succeeded
            ? Results.Created($"/api/roles/{role.Id}", new RoleResponse(role.Id, role.Name!))
            : Results.ValidationProblem(ToErrorDictionary(result));
    }

    private static async Task<IResult> RenameAsync(string id, UpdateRoleRequest request,
        RoleManager<HuiaRole> roleManager)
    {
        var role = await roleManager.FindByIdAsync(id).ConfigureAwait(false);
        if (role is null)
        {
            return Results.NotFound();
        }

        role.Name = request.Name;
        var result = await roleManager.UpdateAsync(role).ConfigureAwait(false);

        return result.Succeeded
            ? Results.Ok(new RoleResponse(role.Id, role.Name!))
            : Results.ValidationProblem(ToErrorDictionary(result));
    }

    private static async Task<IResult> DeleteAsync(string id, RoleManager<HuiaRole> roleManager)
    {
        var role = await roleManager.FindByIdAsync(id).ConfigureAwait(false);
        if (role is null)
        {
            return Results.NotFound();
        }

        var result = await roleManager.DeleteAsync(role).ConfigureAwait(false);
        return result.Succeeded ? Results.NoContent() : Results.ValidationProblem(ToErrorDictionary(result));
    }

    private static async Task<IResult> GetMembersAsync(string id, RoleManager<HuiaRole> roleManager,
        UserManager<HuiaUser> userManager)
    {
        var role = await roleManager.FindByIdAsync(id).ConfigureAwait(false);
        if (role is null)
        {
            return Results.NotFound();
        }

        var members = await userManager.GetUsersInRoleAsync(role.Name!).ConfigureAwait(false);
        return Results.Ok(members.Select(u => new RoleMemberResponse(u.Id, u.Email!)).ToList());
    }

    private static Dictionary<string, string[]> ToErrorDictionary(IdentityResult result)
        => result.Errors
            .GroupBy(e => e.Code, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray(), StringComparer.Ordinal);

    private sealed record RoleResponse(string Id, string Name);

    /// <summary>Mirrors MR.AspNetCore.Pagination's <c>KeysetPaginationResult&lt;T&gt;</c> field-for-field - see
    /// the doc comment on <see cref="ListAsync(RoleManager{HuiaRole})"/>.</summary>
    private sealed record RolesPage<T>(IReadOnlyList<T> Data, int TotalCount, int PageSize, bool HasPrevious,
        bool HasNext);

    private sealed record RoleMemberResponse(string Id, string Email);

    private sealed record CreateRoleRequest(string Name);

    private sealed record UpdateRoleRequest(string Name);
}