using System.Text.RegularExpressions;
using Huia.Entities;
using Huia.Headless.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Huia.Headless.Endpoints;

/// <summary>
/// Administrative endpoints for Huia Headless (<c>/admin/*</c>). The group is returned
/// <em>without</em> an authorization policy — the host attaches its own via
/// <c>MapHuiaHeadlessAdminEndpoints().RequireAuthorization(...)</c>.
/// </summary>
public static partial class AdminEndpoints
{
    [GeneratedRegex("^[A-Za-z0-9._:-]{1,256}$")]
    private static partial Regex RoleNamePattern();

    /// <summary>Maps the admin endpoint group under <c>admin/</c>.</summary>
    internal static RouteGroupBuilder MapHuiaHeadlessAdminEndpointsGroup(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup("admin");

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

        group.MapGet("roles", ListRolesAsync);
        group.MapGet("roles/{id}", GetRoleAsync);
        group.MapPost("roles", CreateRoleAsync);
        group.MapPut("roles/{id}", UpdateRoleAsync);
        group.MapDelete("roles/{id}", DeleteRoleAsync);

        return group;
    }

    private static async Task<IResult> ListUsersAsync(
        HttpContext context,
        HuiaUserManager userManager,
        int? page = 1,
        int? pageSize = 20,
        string? search = null)
    {
        var query = userManager.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(u =>
                (u.Email != null && u.Email.Contains(s)) ||
                (u.UserName != null && u.UserName.Contains(s)) ||
                (u.FirstName != null && u.FirstName.Contains(s)) ||
                (u.LastName != null && u.LastName.Contains(s)) ||
                (u.PhoneNumber != null && u.PhoneNumber.Contains(s)));
        }

        var totalCount = await query.CountAsync(context.RequestAborted);
        var p = Math.Max(1, page ?? 1);
        var size = Math.Clamp(pageSize ?? 20, 1, 100);

        var users = await query
            .OrderBy(u => u.Id)
            .Skip((p - 1) * size)
            .Take(size)
            .ToListAsync(context.RequestAborted);

        var userDtos = new List<HeadlessUserDto>(users.Count);
        foreach (var u in users)
        {
            var roles = await userManager.GetRolesAsync(u);
            userDtos.Add(ToDto(u, roles));
        }

        return Results.Ok(new
        {
            data = userDtos,
            totalCount,
            page = p,
            pageSize = size,
            hasNext = (p * size) < totalCount,
            hasPrevious = p > 1
        });
    }

    private static async Task<IResult> GetUserAsync(
        HttpContext context,
        HuiaUserManager userManager,
        string id)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            return Results.NotFound();
        }

        var roles = await userManager.GetRolesAsync(user);
        return Results.Ok(ToDto(user, roles));
    }

    private static async Task<IResult> CreateUserAsync(
        HttpContext context,
        HuiaUserManager userManager,
        RoleManager<HuiaRole> roleManager,
        CreateHeadlessUserRequest body)
    {
        var hasPassword = !string.IsNullOrWhiteSpace(body.Password);
        var hasEmail = !string.IsNullOrWhiteSpace(body.Email);
        var hasPhone = !string.IsNullOrWhiteSpace(body.PhoneNumber);

        if (hasPhone && (hasEmail || hasPassword))
        {
            return ContactProblem("A phone-login account cannot also have an email address or a password.");
        }

        if (!hasPhone && !(hasEmail && hasPassword))
        {
            return ContactProblem("Provide an email address with a password, or a phone number.");
        }

        var userName = (hasEmail ? body.Email! : body.PhoneNumber!).Trim();
        if (await userManager.FindByNameAsync(userName) is not null)
        {
            return Results.Conflict(new { message = $"A user '{userName}' already exists." });
        }

        var user = new HuiaUser
        {
            UserName = userName,
            Email = hasEmail ? body.Email!.Trim() : null,
            EmailConfirmed = hasEmail && (body.EmailConfirmed ?? false),
            PhoneNumber = hasPhone ? body.PhoneNumber!.Trim() : null,
            PhoneNumberConfirmed = hasPhone,
            FirstName = body.FirstName?.Trim() ?? string.Empty,
            LastName = body.LastName?.Trim() ?? string.Empty,
        };

        var result = hasPassword
            ? await userManager.CreateAsync(user, body.Password!)
            : await userManager.CreateAsync(user);

        if (!result.Succeeded)
        {
            return IdentityProblem(result);
        }

        if (body.Roles is { Length: > 0 })
        {
            foreach (var role in body.Roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new HuiaRole(role) { Origin = HuiaConstants.Origins.Dynamic });
                }

                await userManager.AddToRoleAsync(user, role);
            }
        }

        var roles = await userManager.GetRolesAsync(user);
        return Results.Created($"/admin/users/{Uri.EscapeDataString(user.Id)}", ToDto(user, roles));
    }

    private static async Task<IResult> UpdateUserAsync(
        HttpContext context,
        HuiaUserManager userManager,
        string id,
        UpdateHeadlessUserRequest body)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            return Results.NotFound();
        }

        if (body.FirstName is not null)
        {
            user.FirstName = body.FirstName.Trim();
        }

        if (body.LastName is not null)
        {
            user.LastName = body.LastName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(body.Email))
        {
            user.Email = body.Email.Trim();
            user.UserName = body.Email.Trim();
        }

        if (!string.IsNullOrWhiteSpace(body.PhoneNumber))
        {
            user.PhoneNumber = body.PhoneNumber.Trim();
            if (string.IsNullOrWhiteSpace(user.Email))
            {
                user.UserName = body.PhoneNumber.Trim();
            }
        }

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return IdentityProblem(result);
        }

        var roles = await userManager.GetRolesAsync(user);
        return Results.Ok(ToDto(user, roles));
    }

    private static async Task<IResult> DeleteUserAsync(
        HttpContext context,
        HuiaUserManager userManager,
        string id)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            return Results.NotFound();
        }

        var result = await userManager.DeleteAsync(user);
        return result.Succeeded ? Results.NoContent() : IdentityProblem(result);
    }

    private static async Task<IResult> GetUserRolesAsync(
        HttpContext context,
        HuiaUserManager userManager,
        string id)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            return Results.NotFound();
        }

        var roles = await userManager.GetRolesAsync(user);
        return Results.Ok(roles);
    }

    private static async Task<IResult> AddUserRoleAsync(
        HttpContext context,
        HuiaUserManager userManager,
        RoleManager<HuiaRole> roleManager,
        string id,
        AddUserRoleRequest body)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            return Results.NotFound();
        }

        if (string.IsNullOrWhiteSpace(body.Role))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["role"] = ["Role is required."] });
        }

        if (!await roleManager.RoleExistsAsync(body.Role))
        {
            await roleManager.CreateAsync(new HuiaRole(body.Role) { Origin = HuiaConstants.Origins.Dynamic });
        }

        var result = await userManager.AddToRoleAsync(user, body.Role);
        return result.Succeeded ? Results.Ok() : IdentityProblem(result);
    }

    private static async Task<IResult> RemoveUserRoleAsync(
        HttpContext context,
        HuiaUserManager userManager,
        string id,
        string role)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            return Results.NotFound();
        }

        var result = await userManager.RemoveFromRoleAsync(user, role);
        return result.Succeeded ? Results.NoContent() : IdentityProblem(result);
    }

    private static async Task<IResult> LockUserAsync(
        HttpContext context,
        HuiaUserManager userManager,
        string id)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            return Results.NotFound();
        }

        await userManager.SetLockoutEnabledAsync(user, true);
        var result = await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(100));
        return result.Succeeded ? Results.Ok() : IdentityProblem(result);
    }

    private static async Task<IResult> UnlockUserAsync(
        HttpContext context,
        HuiaUserManager userManager,
        string id)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            return Results.NotFound();
        }

        var result = await userManager.SetLockoutEndDateAsync(user, null);
        return result.Succeeded ? Results.Ok() : IdentityProblem(result);
    }

    private static async Task<IResult> ListRolesAsync(
        HttpContext context,
        RoleManager<HuiaRole> roleManager)
    {
        var roles = await roleManager.Roles.AsNoTracking()
            .Select(r => new HeadlessRoleDto(r.Id, r.Name, r.Origin))
            .ToListAsync(context.RequestAborted);

        return Results.Ok(new { data = roles });
    }

    private static async Task<IResult> GetRoleAsync(
        HttpContext context,
        RoleManager<HuiaRole> roleManager,
        string id)
    {
        var role = await roleManager.FindByIdAsync(id);
        if (role is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new HeadlessRoleDto(role.Id, role.Name, role.Origin));
    }

    private static async Task<IResult> CreateRoleAsync(
        HttpContext context,
        RoleManager<HuiaRole> roleManager,
        CreateHeadlessRoleRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Name) || !RoleNamePattern().IsMatch(body.Name))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["name"] = ["A role name must be 1-256 characters of letters, digits, '.', '_', ':' or '-'."],
            });
        }

        if (await roleManager.RoleExistsAsync(body.Name))
        {
            return Results.Conflict(new { message = $"Role '{body.Name}' already exists." });
        }

        var role = new HuiaRole(body.Name) { Origin = HuiaConstants.Origins.Dynamic };
        var result = await roleManager.CreateAsync(role);
        return result.Succeeded
            ? Results.Created($"/admin/roles/{Uri.EscapeDataString(role.Id)}", new HeadlessRoleDto(role.Id, role.Name, role.Origin))
            : IdentityProblem(result);
    }

    private static async Task<IResult> UpdateRoleAsync(
        HttpContext context,
        RoleManager<HuiaRole> roleManager,
        string id,
        UpdateHeadlessRoleRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Name) || !RoleNamePattern().IsMatch(body.Name))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Invalid role name."] });
        }

        var role = await roleManager.FindByIdAsync(id);
        if (role is null)
        {
            return Results.NotFound();
        }

        if (role.Origin == HuiaConstants.Origins.Static)
        {
            return StaticRoleConflict();
        }

        var result = await roleManager.SetRoleNameAsync(role, body.Name);
        if (result.Succeeded)
        {
            result = await roleManager.UpdateAsync(role);
        }

        return result.Succeeded
            ? Results.Ok(new HeadlessRoleDto(role.Id, role.Name, role.Origin))
            : IdentityProblem(result);
    }

    private static async Task<IResult> DeleteRoleAsync(
        HttpContext context,
        RoleManager<HuiaRole> roleManager,
        string id)
    {
        var role = await roleManager.FindByIdAsync(id);
        if (role is null)
        {
            return Results.NotFound();
        }

        if (role.Origin == HuiaConstants.Origins.Static)
        {
            return StaticRoleConflict();
        }

        var result = await roleManager.DeleteAsync(role);
        return result.Succeeded ? Results.NoContent() : IdentityProblem(result);
    }

    private static HeadlessUserDto ToDto(HuiaUser user, IList<string> roles) =>
        new(user.Id, user.UserName, user.Email, user.EmailConfirmed,
            user.PhoneNumber, user.PhoneNumberConfirmed, user.FirstName, user.LastName,
            user.LockoutEnabled, user.LockoutEnd, [.. roles]);

    private static IResult ContactProblem(string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { ["contact"] = [message] });

    private static IResult StaticRoleConflict() =>
        Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "This role is defined in code and cannot be modified through the admin API.");

    private static IResult IdentityProblem(IdentityResult result)
    {
        var errors = result.Errors
            .GroupBy(e => e.Code)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());
        return Results.ValidationProblem(errors);
    }
}

/// <summary>Represents a user in the headless admin API.</summary>
public sealed record HeadlessUserDto(
    string Id,
    string? UserName,
    string? Email,
    bool EmailConfirmed,
    string? PhoneNumber,
    bool PhoneNumberConfirmed,
    string FirstName,
    string LastName,
    bool LockoutEnabled,
    DateTimeOffset? LockoutEnd,
    string[] Roles);

/// <summary>Represents a role in the headless admin API.</summary>
public sealed record HeadlessRoleDto(
    string Id,
    string? Name,
    string Origin);

/// <summary>Request body for creating a user in the headless admin API.</summary>
public sealed record CreateHeadlessUserRequest(
    string? Email,
    string? Password,
    string? PhoneNumber,
    string? FirstName,
    string? LastName,
    bool? EmailConfirmed,
    string[]? Roles);

/// <summary>Request body for updating a user in the headless admin API.</summary>
public sealed record UpdateHeadlessUserRequest(
    string? FirstName,
    string? LastName,
    string? Email,
    string? PhoneNumber);

/// <summary>Request body for assigning a role to a user.</summary>
public sealed record AddUserRoleRequest(string Role);

/// <summary>Request body for creating a role in the headless admin API.</summary>
public sealed record CreateHeadlessRoleRequest(string Name);

/// <summary>Request body for updating a role in the headless admin API.</summary>
public sealed record UpdateHeadlessRoleRequest(string Name);
