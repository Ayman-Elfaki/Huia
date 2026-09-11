using Huia.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MR.AspNetCore.Pagination;

namespace Huia.Endpoints;

/// <summary>
/// Administrative endpoints (<c>/admin/*</c>). The group is returned <em>without</em> an
/// authorization policy — the host attaches its own.
/// Lists use keyset (cursor) pagination via <c>MR.AspNetCore.Pagination</c>, never offset.
/// </summary>
internal static partial class AdminEndpoints
{
    public static RouteGroupBuilder MapHuiaAdminEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("admin");
        return MapHuiaAdminEndpoints(group);
    }

    public static RouteGroupBuilder MapHuiaAdminEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("tenants", ListTenants);

        group.MapGet("users", ListUsersAsync);
        group.MapGet("users/{id}", GetUserAsync);
        group.MapPost("users", CreateUserAsync);
        group.MapPut("users/{id}", UpdateUserAsync);
        group.MapDelete("users/{id}", DeleteUserAsync);
        group.MapGet("users/{id}/roles", GetUserRolesAsync);
        group.MapPost("users/{id}/roles", AddUserRoleAsync);
        group.MapDelete("users/{id}/roles/{role}", RemoveUserRoleAsync);
        group.MapPost("users/{id}/lock", LockUserAsync);
        group.MapPost("users/{id}/unlock", UnlockUserAsync);
        group.MapPost("users/{id}/verify-email", VerifyEmailAsync);

        group.MapGet("roles", ListRolesAsync);
        group.MapGet("roles/{id}", GetRoleAsync);
        group.MapPost("roles", CreateRoleAsync);
        group.MapPut("roles/{id}", UpdateRoleAsync);
        group.MapDelete("roles/{id}", DeleteRoleAsync);

        return group;
    }

    private static IResult ListTenants(HuiaOptions options)
    {
        var tenants = options.Tenants
            .Select(kvp => new TenantDto(
                kvp.Key,
                kvp.Value.Branding.DisplayName ?? kvp.Value.DisplayName ?? kvp.Key,
                kvp.Value.Authentication.IsEmailAndPasswordLoginEnabled,
                kvp.Value.Authentication.IsPhoneLoginEnabled,
                kvp.Value.Roles.Count))
            .OrderBy(t => t.TenantId, StringComparer.Ordinal)
            .ToList();

        return Results.Ok(tenants);
    }

    private static async Task<IResult> ListUsersAsync(
        HttpContext context,
        IHuiaAdminStore adminStore)
    {
        var tenant = context.Request.Query["tenant"].ToString();
        var query = ReadQuery(context);

        var result = await adminStore.ListUsersAsync(
            string.IsNullOrEmpty(tenant) ? null : tenant,
            query,
            context.RequestAborted);

        return Results.Ok(new { data = result.Data, result.HasNext, result.HasPrevious });
    }

    private static KeysetQueryModel ReadQuery(HttpContext context)
    {
        var query = context.Request.Query;
        var after = query["after"].ToString();
        var before = query["before"].ToString();
        var size = int.TryParse(query["size"], out var parsed) ? Math.Clamp(parsed, 1, 100) : 25;

        return new KeysetQueryModel
        {
            Size = size,
            After = string.IsNullOrEmpty(after) ? null : after,
            Before = string.IsNullOrEmpty(before) ? null : before,
            First = string.IsNullOrEmpty(after) && string.IsNullOrEmpty(before),
            Last = false,
        };
    }

    private sealed record TenantDto(
        string TenantId,
        string DisplayName,
        bool PasswordEnabled,
        bool PhoneLoginEnabled,
        int RoleCount);
}
