using Huia.Common;
using Huia.Identity;
using Huia.Pagination;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
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

    private static async Task<IResult> ListAsync(string? search, string? cursor, int? pageSize,
        UserManager<HuiaUser> userManager, [FromServices] IAdminEfCorePaginator? paginator, CancellationToken ct)
    {
        var size = pageSize is null or <= 0 or > 100 ? 25 : pageSize.Value;

        if (paginator is not null)
        {
            return await paginator.ListUsersAsync(search, size, ct).ConfigureAwait(false);
        }

        if (!userManager.SupportsQueryableUsers)
        {
            return Results.Problem("The configured user store doesn't support listing users.",
                statusCode: StatusCodes.Status501NotImplemented);
        }

        var offset = OffsetCursor.Decode(cursor);

        var query = userManager.Users;
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(u => u.Email!.Contains(search) || u.UserName!.Contains(search));
        }

        // UserManager.Users is a plain IQueryable<TUser> with no guaranteed async provider (a custom,
        // non-EF-Core IUserStore might back it with something that isn't IAsyncQueryProvider) — enumerating
        // synchronously here trades a blocked thread for staying store-agnostic. Acceptable for an
        // admin-only, low-traffic surface. Fetches one extra row (rather than a separate LongCount() call)
        // to know whether a next page exists - see OffsetCursor's own doc comment for why this stays
        // offset-based internally instead of a real keyset query.
        var page1 = query.OrderBy(u => u.Email).Skip(offset).Take(size + 1).ToList();

        var nextCursor = page1.Count > size ? OffsetCursor.Encode(offset + size) : null;
        var items = new List<UserResponse>(size);
        foreach (var user in page1.Take(size))
        {
            items.Add(await ToResponseAsync(user, userManager).ConfigureAwait(false));
        }

        return Results.Ok(new PagedResult<UserResponse>(items, nextCursor));
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
        var nameErrors = ValidateNames(request.FirstName, request.LastName);
        if (nameErrors is not null)
        {
            return Results.ValidationProblem(nameErrors);
        }

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

        var nameErrors = ValidateNames(request.FirstName, request.LastName);
        if (nameErrors is not null)
        {
            return Results.ValidationProblem(nameErrors);
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

    internal static async Task<UserResponse> ToResponseAsync(HuiaUser user, UserManager<HuiaUser> userManager)
    {
        var roles = await userManager.GetRolesAsync(user).ConfigureAwait(false);
        var logins = await userManager.GetLoginsAsync(user).ConfigureAwait(false);

        return new UserResponse(
            user.Id,
            user.Email!,
            user.FirstName,
            user.LastName,
            user.Picture,
            user.EmailConfirmed,
            user.PhoneNumber,
            user.LockoutEnabled && user.LockoutEnd is not null && user.LockoutEnd > DateTimeOffset.UtcNow,
            user.TwoFactorEnabled,
            await userManager.HasPasswordAsync(user).ConfigureAwait(false),
            [.. roles],
            [.. logins.Select(l => new ExternalLoginResponse(l.LoginProvider, l.ProviderDisplayName))]);
    }

    private static Dictionary<string, string[]> ToErrorDictionary(IdentityResult result)
        => result.Errors
            .GroupBy(e => e.Code, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray(), StringComparer.Ordinal);

    /// <summary>Validates a create/update request's names against <see cref="PersonNameValidator"/> - required,
    /// at most <see cref="PersonNameValidator.MaxLength"/> characters, letters/spaces/hyphens/apostrophes only.
    /// Returns <see langword="null"/> if both are valid, or a <see cref="Results.ValidationProblem"/>-ready
    /// dictionary otherwise. Minimal API records aren't DataAnnotations-validated automatically in this
    /// codebase (unlike the Identity UI's Razor Page InputModels, see <c>PersonNameAttribute</c>), so this
    /// endpoint checks explicitly.</summary>
    private static Dictionary<string, string[]>? ValidateNames(string? firstName, string? lastName)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (!PersonNameValidator.IsValid(firstName))
        {
            errors[nameof(CreateUserRequest.FirstName)] = [PersonNameValidator.DescribeError("First name")];
        }

        if (!PersonNameValidator.IsValid(lastName))
        {
            errors[nameof(CreateUserRequest.LastName)] = [PersonNameValidator.DescribeError("Last name")];
        }

        return errors.Count > 0 ? errors : null;
    }

    internal sealed record UserResponse(
        string Id,
        string Email,
        string? FirstName,
        string? LastName,
        string? Picture,
        bool EmailConfirmed,
        string? PhoneNumber,
        bool IsLockedOut,
        bool TwoFactorEnabled,
        bool HasPassword,
        string[] Roles,
        ExternalLoginResponse[] ExternalLogins);

    /// <summary>Which external (third-party) provider(s) a user has signed in with — the admin-facing view
    /// of the same data <c>ManageExternalLoginsEndpoints</c> exposes for self-service.</summary>
    internal sealed record ExternalLoginResponse(string LoginProvider, string? ProviderDisplayName);

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