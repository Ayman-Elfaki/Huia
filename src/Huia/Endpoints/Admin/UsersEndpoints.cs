using Huia.Common;
using Huia.Identity;
using Huia.Pagination;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Huia.Endpoints.Admin;

/// <summary>
/// JSON CRUD endpoints over <see cref="HuiaUser"/>, backed by <see cref="HuiaUserManager"/>.
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
        HuiaUserManager userManager, [FromServices] IAdminEfCorePaginator? paginator, CancellationToken ct)
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

        // Non-null: SupportsQueryableUsers (checked above) is exactly "Users is not null".
        var query = userManager.Users!;
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(u => u.Email!.Contains(search) || u.UserName!.Contains(search));
        }

        // UserManager.Users is a plain IQueryable<TUser> with no guaranteed async provider (a custom,
        // non-EF-Core IUserStore might back it with something that isn't IAsyncQueryProvider) — enumerating
        // synchronously here trades a blocked thread for staying store-agnostic. Acceptable for an
        // admin-only, low-traffic surface. Fetches one extra row (rather than inferring it from TotalCount)
        // to know whether a next page exists - see OffsetCursor's own doc comment for why this stays
        // offset-based internally instead of a real keyset query.
        var page1 = query.OrderBy(u => u.Email).Skip(offset).Take(size + 1).ToList();

        var nextCursor = page1.Count > size ? OffsetCursor.Encode(offset + size) : null;
        var items = new List<UserResponse>(size);
        foreach (var user in page1.Take(size))
        {
            items.Add(await ToResponseAsync(user, userManager).ConfigureAwait(false));
        }

        var totalCount = query.Count();
        return Results.Ok(new PagedResult<UserResponse>(items, totalCount, size, offset > 0, nextCursor is not null,
            nextCursor));
    }

    private static async Task<IResult> GetAsync(string id, HuiaUserManager userManager)
    {
        var user = await userManager.FindByIdAsync(id).ConfigureAwait(false);
        return user is null
            ? Results.NotFound()
            : Results.Ok(await ToResponseAsync(user, userManager).ConfigureAwait(false));
    }

    private static async Task<IResult> CreateAsync(CreateUserRequest request, HuiaUserManager userManager,
        HuiaOptions options)
    {
        var nameErrors = ValidateNames(request.FirstName, request.LastName);
        if (nameErrors is not null)
        {
            return Results.ValidationProblem(nameErrors);
        }

        var hasEmail = !string.IsNullOrWhiteSpace(request.Email);
        var hasPhone = !string.IsNullOrWhiteSpace(request.PhoneNumber);
        var identifierErrors = ValidateIdentifierChoice(hasEmail, hasPhone, options);
        if (identifierErrors is not null)
        {
            return Results.ValidationProblem(identifierErrors);
        }

        if (hasPhone)
        {
            return await CreatePhoneUserAsync(request, userManager).ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [nameof(CreateUserRequest.Password)] = ["Password is required."],
            });
        }

        var user = new HuiaUser
        {
            UserName = request.Email,
            Email = request.Email,
            EmailConfirmed = request.EmailConfirmed,
            FirstName = request.FirstName,
            LastName = request.LastName,
        };

        var result = await userManager.CreateAsync(user, request.Password!).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return Results.ValidationProblem(ToErrorDictionary(result));
        }

        return Results.Created($"/api/users/{user.Id}", await ToResponseAsync(user, userManager).ConfigureAwait(false));
    }

    /// <summary>
    /// Creates a phone-only account, the admin-initiated equivalent of what <c>PhoneLoginModel</c> creates on
    /// a brand-new number's first OTP request — same shape (<see cref="HuiaUser.PasswordlessLoginEnabled"/>
    /// set, no password), except <see cref="HuiaUser.PhoneNumberConfirmed"/> is set immediately: the
    /// admin is vouching for the number directly (typing it in), the same trust an admin already gets for
    /// email via <see cref="CreateUserRequest.EmailConfirmed"/>, rather than proving it via OTP.
    /// </summary>
    private static async Task<IResult> CreatePhoneUserAsync(CreateUserRequest request, HuiaUserManager userManager)
    {
        var normalized = PhoneNumberValidator.TryNormalizeE164(request.PhoneNumber);
        if (normalized is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [nameof(CreateUserRequest.PhoneNumber)] =
                    ["Enter a valid phone number in international format, e.g. +15551234567."],
            });
        }

        var user = new HuiaUser
        {
            UserName = normalized,
            PhoneNumber = normalized,
            NormalizedPhoneNumber = normalized,
            PhoneNumberConfirmed = true,
            PasswordlessLoginEnabled = true,
            FirstName = request.FirstName,
            LastName = request.LastName,
        };

        var result = await userManager.CreateAsync(user).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return Results.ValidationProblem(ToErrorDictionary(result));
        }

        return Results.Created($"/api/users/{user.Id}", await ToResponseAsync(user, userManager).ConfigureAwait(false));
    }

    private static async Task<IResult> UpdateAsync(string id, UpdateUserRequest request,
        HuiaUserManager userManager)
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

        // Blank/absent for a phone-only user reaching this endpoint through the same shared edit form -
        // leave Email/UserName alone rather than nulling them out and breaking that account's sign-in.
        if (!string.IsNullOrWhiteSpace(request.Email) &&
            !string.Equals(request.Email, user.Email, StringComparison.OrdinalIgnoreCase))
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

    private static async Task<IResult> DeleteAsync(string id, HuiaUserManager userManager)
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
        HuiaUserManager userManager, HuiaMembershipAdmin membershipAdmin)
    {
        var user = await userManager.FindByIdAsync(id).ConfigureAwait(false);
        if (user is null)
        {
            return Results.NotFound();
        }

        var result = request.Add
            ? await membershipAdmin.AddUserToRoleAsync(user, request.Role).ConfigureAwait(false)
            : await membershipAdmin.RemoveUserFromRoleAsync(user, request.Role).ConfigureAwait(false);

        return result.Succeeded
            ? Results.Ok(await ToResponseAsync(user, userManager).ConfigureAwait(false))
            : Results.ValidationProblem(ToErrorDictionary(result));
    }

    private static async Task<IResult> SetLockoutAsync(string id, SetLockoutRequest request,
        HuiaUserManager userManager, HuiaMembershipAdmin membershipAdmin)
    {
        var user = await userManager.FindByIdAsync(id).ConfigureAwait(false);
        if (user is null)
        {
            return Results.NotFound();
        }

        var result = await membershipAdmin.SetLockedAsync(user, request.Locked).ConfigureAwait(false);

        return result.Succeeded
            ? Results.Ok(await ToResponseAsync(user, userManager).ConfigureAwait(false))
            : Results.ValidationProblem(ToErrorDictionary(result));
    }

    private static async Task<IResult> ResetPasswordAsync(string id, ResetPasswordRequest request,
        HuiaUserManager userManager)
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

    internal static async Task<UserResponse> ToResponseAsync(HuiaUser user, HuiaUserManager userManager)
    {
        var roles = await userManager.GetRolesAsync(user).ConfigureAwait(false);
        var logins = await userManager.GetLoginsAsync(user).ConfigureAwait(false);
        var hasPassword = await userManager.HasPasswordAsync(user).ConfigureAwait(false);

        // Every way this account can actually sign in - not necessarily just one, e.g. a password-registered
        // account that later linked Google shows both.
        var authenticationMethods = new List<string>();
        if (hasPassword)
        {
            authenticationMethods.Add("email");
        }

        if (user.PasswordlessLoginEnabled)
        {
            authenticationMethods.Add("phone");
        }

        authenticationMethods.AddRange(logins.Select(l => l.LoginProvider.ToLowerInvariant()));

        return new UserResponse(
            user.Id,
            user.Email,
            user.UserName,
            user.FirstName,
            user.LastName,
            user.Picture,
            user.EmailConfirmed,
            user.PhoneNumber,
            user.LockoutEnabled && user.LockoutEnd is not null && user.LockoutEnd > DateTimeOffset.UtcNow,
            user.TwoFactorEnabled,
            hasPassword,
            [.. roles],
            [.. authenticationMethods],
            [.. logins.Select(l => new ExternalLoginResponse(l.LoginProvider, l.ProviderDisplayName))]);
    }

    private static Dictionary<string, string[]> ToErrorDictionary(HuiaIdentityResult result)
        => result.Errors
            .GroupBy(e => e.Code, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray(), StringComparer.Ordinal);

    /// <summary>
    /// Validates a create request's choice of identifier: exactly one of email or phone number must be
    /// given, and it must belong to a sign-in flow this app actually has enabled
    /// (<c>huia.Authentication.Use...Flow()</c>) - creating an account with an identifier no enabled flow can
    /// sign in with would produce an account that can never actually be used. Returns <see langword="null"/>
    /// if the choice is valid, or a <see cref="Results.ValidationProblem"/>-ready dictionary otherwise.
    /// </summary>
    internal static Dictionary<string, string[]>? ValidateIdentifierChoice(bool hasEmail, bool hasPhone,
        HuiaOptions options)
    {
        if (hasEmail == hasPhone)
        {
            return new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [nameof(CreateUserRequest.Email)] =
                    ["Provide either an email and password, or a phone number, but not both."],
            };
        }

        if (hasPhone && !options.Authentication.PasswordlessFlowEnabled)
        {
            return new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [nameof(CreateUserRequest.PhoneNumber)] = ["Phone-based sign-in isn't enabled for this app."],
            };
        }

        if (hasEmail && !options.Authentication.EmailAndPasswordFlowEnabled)
        {
            return new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [nameof(CreateUserRequest.Email)] = ["Email/password sign-in isn't enabled for this app."],
            };
        }

        return null;
    }

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
        string? Email,
        string? UserName,
        string? FirstName,
        string? LastName,
        string? Picture,
        bool EmailConfirmed,
        string? PhoneNumber,
        bool IsLockedOut,
        bool TwoFactorEnabled,
        bool HasPassword,
        string[] Roles,
        string[] AuthenticationMethods,
        ExternalLoginResponse[] ExternalLogins);

    /// <summary>Which external (third-party) provider(s) a user has signed in with — the admin-facing view
    /// of the same data <c>ManageExternalLoginsEndpoints</c> exposes for self-service.</summary>
    internal sealed record ExternalLoginResponse(string LoginProvider, string? ProviderDisplayName);

    /// <summary>Either <see cref="Email"/>+<see cref="Password"/> (a password-based account) or
    /// <see cref="PhoneNumber"/> (a passwordless phone account, in international format e.g.
    /// <c>+15551234567</c>) must be supplied, never both — see <c>CreateAsync</c>.</summary>
    private sealed record CreateUserRequest(
        string? Email,
        string? Password,
        string? PhoneNumber,
        string? FirstName,
        string? LastName,
        bool EmailConfirmed);

    private sealed record UpdateUserRequest(string? Email, string? FirstName, string? LastName, bool EmailConfirmed);

    private sealed record UpdateUserRoleRequest(string Role, bool Add);

    private sealed record SetLockoutRequest(bool Locked);

    private sealed record ResetPasswordRequest(string NewPassword);
}