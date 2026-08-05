using Huia.Common;
using Huia.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;

namespace Huia.Endpoints.Admin;

/// <summary>
/// JSON CRUD endpoints over <see cref="HuiaUser"/>, backed by <see cref="UserManager{TUser}"/>.
/// </summary>
internal static class UsersEndpoints
{
    public static IEndpointRouteBuilder MapUsersEndpoints(this IEndpointRouteBuilder api)
    {
        var users = api.MapGroup("/users");

        users.MapGet("", ListAsync);
        users.MapGet("/{id}", GetAsync);
        users.MapPost("", CreateAsync);
        users.MapPut("/{id}", UpdateAsync);
        users.MapDelete("/{id}", DeleteAsync);
        users.MapPost("/{id}/roles", UpdateRolesAsync);
        users.MapPost("/{id}/lockout", SetLockoutAsync);
        users.MapPost("/{id}/reset-password", ResetPasswordAsync);

        return api;
    }

    private static async Task<IResult> ListAsync(string? search, int? page, int? pageSize,
        UserManager<HuiaUser> userManager)
    {
        if (!userManager.SupportsQueryableUsers)
        {
            return Results.Problem("The configured user store doesn't support listing users.",
                statusCode: StatusCodes.Status501NotImplemented);
        }

        var size = pageSize is null or <= 0 or > 100 ? 25 : pageSize.Value;
        var currentPage = page is null or <= 0 ? 1 : page.Value;

        var query = userManager.Users;
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(u => u.Email!.Contains(search) || u.UserName!.Contains(search));
        }

        // UserManager.Users is a plain IQueryable<TUser> with no guaranteed async provider (a custom,
        // non-EF-Core IUserStore might back it with something that isn't IAsyncQueryProvider) — enumerating
        // synchronously here trades a blocked thread for staying store-agnostic. Acceptable for an
        // admin-only, low-traffic surface.
        var totalCount = query.LongCount();
        var page1 = query.OrderBy(u => u.Email).Skip((currentPage - 1) * size).Take(size).ToList();

        var items = new List<UserResponse>(page1.Count);
        foreach (var user in page1)
        {
            items.Add(await ToResponseAsync(user, userManager).ConfigureAwait(false));
        }

        return Results.Ok(new PagedResult<UserResponse>(items, totalCount));
    }

    private static async Task<IResult> GetAsync(string id, UserManager<HuiaUser> userManager)
    {
        var user = await userManager.FindByIdAsync(id).ConfigureAwait(false);
        return user is null
            ? Results.NotFound()
            : Results.Ok(await ToResponseAsync(user, userManager).ConfigureAwait(false));
    }

    private static async Task<IResult> CreateAsync(CreateUserRequest request, UserManager<HuiaUser> userManager)
    {
        var user = new HuiaUser
        {
            UserName = request.Email,
            Email = request.Email,
            EmailConfirmed = request.EmailConfirmed,
            FirstName = request.FirstName,
            LastName = request.LastName,
        };

        var result = await userManager.CreateAsync(user, request.Password).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return Results.ValidationProblem(ToErrorDictionary(result));
        }

        return Results.Created($"/api/users/{user.Id}", await ToResponseAsync(user, userManager).ConfigureAwait(false));
    }

    private static async Task<IResult> UpdateAsync(string id, UpdateUserRequest request,
        UserManager<HuiaUser> userManager)
    {
        var user = await userManager.FindByIdAsync(id).ConfigureAwait(false);
        if (user is null)
        {
            return Results.NotFound();
        }

        user.FirstName = request.FirstName;
        user.LastName = request.LastName;

        if (!string.Equals(request.Email, user.Email, StringComparison.OrdinalIgnoreCase))
        {
            user.Email = request.Email;
            user.UserName = request.Email;
            user.EmailConfirmed = request.EmailConfirmed;
        }

        var result = await userManager.UpdateAsync(user).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return Results.ValidationProblem(ToErrorDictionary(result));
        }

        return Results.Ok(await ToResponseAsync(user, userManager).ConfigureAwait(false));
    }

    private static async Task<IResult> DeleteAsync(string id, UserManager<HuiaUser> userManager)
    {
        var user = await userManager.FindByIdAsync(id).ConfigureAwait(false);
        if (user is null)
        {
            return Results.NotFound();
        }

        var result = await userManager.DeleteAsync(user).ConfigureAwait(false);
        return result.Succeeded ? Results.NoContent() : Results.ValidationProblem(ToErrorDictionary(result));
    }

    private static async Task<IResult> UpdateRolesAsync(string id, UpdateUserRoleRequest request,
        UserManager<HuiaUser> userManager)
    {
        var user = await userManager.FindByIdAsync(id).ConfigureAwait(false);
        if (user is null)
        {
            return Results.NotFound();
        }

        var result = request.Add
            ? await userManager.AddToRoleAsync(user, request.Role).ConfigureAwait(false)
            : await userManager.RemoveFromRoleAsync(user, request.Role).ConfigureAwait(false);

        return result.Succeeded
            ? Results.Ok(await ToResponseAsync(user, userManager).ConfigureAwait(false))
            : Results.ValidationProblem(ToErrorDictionary(result));
    }

    private static async Task<IResult> SetLockoutAsync(string id, SetLockoutRequest request,
        UserManager<HuiaUser> userManager)
    {
        var user = await userManager.FindByIdAsync(id).ConfigureAwait(false);
        if (user is null)
        {
            return Results.NotFound();
        }

        await userManager.SetLockoutEnabledAsync(user, true).ConfigureAwait(false);
        var result = await userManager.SetLockoutEndDateAsync(user, request.Locked ? DateTimeOffset.MaxValue : null)
            .ConfigureAwait(false);

        return result.Succeeded
            ? Results.Ok(await ToResponseAsync(user, userManager).ConfigureAwait(false))
            : Results.ValidationProblem(ToErrorDictionary(result));
    }

    private static async Task<IResult> ResetPasswordAsync(string id, ResetPasswordRequest request,
        UserManager<HuiaUser> userManager)
    {
        var user = await userManager.FindByIdAsync(id).ConfigureAwait(false);
        if (user is null)
        {
            return Results.NotFound();
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user).ConfigureAwait(false);
        var result = await userManager.ResetPasswordAsync(user, token, request.NewPassword).ConfigureAwait(false);

        return result.Succeeded ? Results.NoContent() : Results.ValidationProblem(ToErrorDictionary(result));
    }

    private static async Task<UserResponse> ToResponseAsync(HuiaUser user, UserManager<HuiaUser> userManager)
    {
        var roles = await userManager.GetRolesAsync(user).ConfigureAwait(false);

        return new UserResponse(
            user.Id,
            user.Email!,
            user.FirstName,
            user.LastName,
            user.EmailConfirmed,
            user.PhoneNumber,
            user.LockoutEnabled && user.LockoutEnd is not null && user.LockoutEnd > DateTimeOffset.UtcNow,
            user.TwoFactorEnabled,
            [.. roles]);
    }

    private static Dictionary<string, string[]> ToErrorDictionary(IdentityResult result)
        => result.Errors
            .GroupBy(e => e.Code)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());

    private sealed record UserResponse(
        string Id,
        string Email,
        string? FirstName,
        string? LastName,
        bool EmailConfirmed,
        string? PhoneNumber,
        bool IsLockedOut,
        bool TwoFactorEnabled,
        string[] Roles);

    private sealed record CreateUserRequest(
        string Email,
        string Password,
        string? FirstName,
        string? LastName,
        bool EmailConfirmed);

    private sealed record UpdateUserRequest(string Email, string? FirstName, string? LastName, bool EmailConfirmed);

    private sealed record UpdateUserRoleRequest(string Role, bool Add);

    private sealed record SetLockoutRequest(bool Locked);

    private sealed record ResetPasswordRequest(string NewPassword);
}