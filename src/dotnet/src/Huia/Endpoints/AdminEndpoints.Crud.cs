using Huia.Identity;
using Huia.Multitenancy;
using Huia.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.Endpoints;

/// <summary>CRUD for users in the admin API. See <see cref="AdminEndpoints"/>.</summary>
internal static partial class AdminEndpoints
{
    private static async Task<IResult> GetUserAsync(IHuiaAdminStore adminStore, string id, CancellationToken ct)
    {
        var user = await adminStore.GetUserAsync(id, ct);
        return user is null ? Results.NotFound() : Results.Ok(user);
    }

    private static async Task<IResult> CreateUserAsync(HttpContext context, HuiaOptions options, CreateUserRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Tenant) || !options.Tenants.ContainsKey(body.Tenant))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["tenant"] = ["Unknown tenant."] });
        }

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

        return await WithTenantScopeAsync(context, body.Tenant, async services =>
        {
            var userManager = services.GetRequiredService<HuiaUserManager>();
            var userName = (hasEmail ? body.Email! : body.PhoneNumber!).Trim();

            if (await userManager.FindByNameAsync(userName) is not null)
            {
                return Results.Conflict(new { message = $"A user '{userName}' already exists in tenant '{body.Tenant}'." });
            }

            var user = new HuiaUser
            {
                TenantId = body.Tenant,
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

            foreach (var role in body.Roles ?? [])
            {
                var roleManager = services.GetRequiredService<RoleManager<HuiaRole>>();
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new HuiaRole(role) { TenantId = body.Tenant });
                }

                await userManager.AddToRoleAsync(user, role);
            }

            return Results.Created(
                $"/admin/users/{user.Id}",
                new UserDto(user.Id, user.TenantId, user.UserName, user.Email, user.EmailConfirmed,
                    user.PhoneNumber, user.PhoneNumberConfirmed, user.LockoutEnabled, user.LockoutEnd,
                    body.Roles ?? Array.Empty<string>()));
        });
    }

    private static async Task<IResult> UpdateUserAsync(
        HttpContext context, IHuiaAdminStore adminStore, string id, UpdateUserRequest body)
    {
        var tenantId = await adminStore.GetUserTenantIdAsync(id, context.RequestAborted);
        if (tenantId is null)
        {
            return Results.NotFound();
        }

        return await WithTenantScopeAsync(context, tenantId, async services =>
        {
            var userManager = services.GetRequiredService<HuiaUserManager>();
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

            if (body.Email is not null)
            {
                if (await userManager.GetUserTypeAsync(user) == HuiaUserType.Phone)
                {
                    return ContactProblem("A phone-login account cannot have an email address.");
                }

                user.Email = body.Email.Trim();
                user.UserName = body.Email.Trim();
                user.NormalizedEmail = userManager.NormalizeEmail(user.Email);
                user.NormalizedUserName = userManager.NormalizeName(user.UserName);
            }

            if (body.EmailConfirmed.HasValue)
            {
                user.EmailConfirmed = body.EmailConfirmed.Value;
            }

            if (body.PhoneNumber is not null)
            {
                if (await userManager.GetUserTypeAsync(user) == HuiaUserType.Password)
                {
                    return ContactProblem("An email-and-password account cannot change its identity phone number here.");
                }

                user.PhoneNumber = body.PhoneNumber.Trim();
                user.UserName = body.PhoneNumber.Trim();
                user.NormalizedUserName = userManager.NormalizeName(user.UserName);
            }

            if (body.PhoneNumberConfirmed.HasValue)
            {
                user.PhoneNumberConfirmed = body.PhoneNumberConfirmed.Value;
            }

            var result = await userManager.UpdateAsync(user);
            return result.Succeeded ? Results.NoContent() : IdentityProblem(result);
        });
    }

    private static async Task<IResult> DeleteUserAsync(HttpContext context, IHuiaAdminStore adminStore, string id)
    {
        var tenantId = await adminStore.GetUserTenantIdAsync(id, context.RequestAborted);
        if (tenantId is null)
        {
            return Results.NotFound();
        }

        return await WithTenantScopeAsync(context, tenantId, async services =>
        {
            var userManager = services.GetRequiredService<HuiaUserManager>();
            var user = await userManager.FindByIdAsync(id);
            if (user is null)
            {
                return Results.NotFound();
            }

            var result = await userManager.DeleteAsync(user);
            return result.Succeeded ? Results.NoContent() : IdentityProblem(result);
        });
    }

    private static async Task<IResult> GetUserRolesAsync(IHuiaAdminStore adminStore, string id, CancellationToken ct)
    {
        var roles = await adminStore.GetUserRolesAsync(id, ct);
        return Results.Ok(roles);
    }

    private static async Task<IResult> AddUserRoleAsync(
        HttpContext context, IHuiaAdminStore adminStore, string id, AddUserRoleRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Role))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["role"] = ["Is required."] });
        }

        var tenantId = await adminStore.GetUserTenantIdAsync(id, context.RequestAborted);
        if (tenantId is null)
        {
            return Results.NotFound();
        }

        return await WithTenantScopeAsync(context, tenantId, async services =>
        {
            var userManager = services.GetRequiredService<HuiaUserManager>();
            var user = await userManager.FindByIdAsync(id);
            if (user is null)
            {
                return Results.NotFound();
            }

            var roleManager = services.GetRequiredService<RoleManager<HuiaRole>>();
            if (!await roleManager.RoleExistsAsync(body.Role))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["role"] = ["Unknown role."] });
            }

            var result = await userManager.AddToRoleAsync(user, body.Role);
            return result.Succeeded ? Results.NoContent() : IdentityProblem(result);
        });
    }

    private static async Task<IResult> RemoveUserRoleAsync(
        HttpContext context, IHuiaAdminStore adminStore, string id, string role)
    {
        var tenantId = await adminStore.GetUserTenantIdAsync(id, context.RequestAborted);
        if (tenantId is null)
        {
            return Results.NotFound();
        }

        return await WithTenantScopeAsync(context, tenantId, async services =>
        {
            var userManager = services.GetRequiredService<HuiaUserManager>();
            var user = await userManager.FindByIdAsync(id);
            if (user is null)
            {
                return Results.NotFound();
            }

            var result = await userManager.RemoveFromRoleAsync(user, role);
            return result.Succeeded ? Results.NoContent() : IdentityProblem(result);
        });
    }

    private static async Task<IResult> LockUserAsync(
        HttpContext context, IHuiaAdminStore adminStore, string id, LockUserRequest? body)
    {
        var tenantId = await adminStore.GetUserTenantIdAsync(id, context.RequestAborted);
        if (tenantId is null)
        {
            return Results.NotFound();
        }

        return await WithTenantScopeAsync(context, tenantId, async services =>
        {
            var userManager = services.GetRequiredService<HuiaUserManager>();
            var user = await userManager.FindByIdAsync(id);
            if (user is null)
            {
                return Results.NotFound();
            }

            await userManager.SetLockoutEnabledAsync(user, true);
            var result = await userManager.SetLockoutEndDateAsync(user, body?.Until ?? DateTimeOffset.MaxValue);
            return result.Succeeded ? Results.NoContent() : IdentityProblem(result);
        });
    }

    private static async Task<IResult> UnlockUserAsync(HttpContext context, IHuiaAdminStore adminStore, string id)
    {
        var tenantId = await adminStore.GetUserTenantIdAsync(id, context.RequestAborted);
        if (tenantId is null)
        {
            return Results.NotFound();
        }

        return await WithTenantScopeAsync(context, tenantId, async services =>
        {
            var userManager = services.GetRequiredService<HuiaUserManager>();
            var user = await userManager.FindByIdAsync(id);
            if (user is null)
            {
                return Results.NotFound();
            }

            var result = await userManager.SetLockoutEndDateAsync(user, null);
            if (!result.Succeeded)
            {
                return IdentityProblem(result);
            }

            await userManager.ResetAccessFailedCountAsync(user);
            return Results.NoContent();
        });
    }

    private static async Task<IResult> VerifyEmailAsync(HttpContext context, IHuiaAdminStore adminStore, string id)
    {
        var tenantId = await adminStore.GetUserTenantIdAsync(id, context.RequestAborted);
        if (tenantId is null)
        {
            return Results.NotFound();
        }

        return await WithTenantScopeAsync(context, tenantId, async services =>
        {
            var userManager = services.GetRequiredService<HuiaUserManager>();
            var user = await userManager.FindByIdAsync(id);
            if (user is null)
            {
                return Results.NotFound();
            }

            if (await userManager.GetUserTypeAsync(user) != HuiaUserType.Password)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["email"] = ["Only an email-and-password account has an email address to verify."],
                });
            }

            if (user.EmailConfirmed)
            {
                return Results.NoContent();
            }

            user.EmailConfirmed = true;
            var result = await userManager.UpdateAsync(user);
            return result.Succeeded ? Results.NoContent() : IdentityProblem(result);
        });
    }

    private static async Task<IResult> WithTenantScopeAsync(
        HttpContext context, string tenantId, Func<IServiceProvider, Task<IResult>> work)
    {
        await using var scope = context.RequestServices.CreateAsyncScope();
        using (HuiaTenantScope.Enter(scope.ServiceProvider, tenantId))
        {
            return await work(scope.ServiceProvider);
        }
    }

    private static IResult ContactProblem(string message)
        => Results.ValidationProblem(new Dictionary<string, string[]> { ["contact"] = [message] });

    private static IResult IdentityProblem(IdentityResult result)
        => Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["identity"] = result.Errors.Select(e => e.Description).ToArray(),
        });

    private sealed record CreateUserRequest(
        string Tenant,
        string? Email,
        string? Password,
        string? PhoneNumber,
        string? FirstName,
        string? LastName,
        bool? EmailConfirmed,
        string[]? Roles);

    private sealed record UpdateUserRequest(
        string? FirstName,
        string? LastName,
        string? Email,
        bool? EmailConfirmed,
        string? PhoneNumber,
        bool? PhoneNumberConfirmed);

    private sealed record AddUserRoleRequest(string Role);

    private sealed record LockUserRequest(DateTimeOffset? Until);
}
