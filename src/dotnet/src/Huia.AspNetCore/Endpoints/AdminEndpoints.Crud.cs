using Huia.AspNetCore.Identity;
using Huia.AspNetCore.Keys;
using Huia.AspNetCore.Multitenancy;
using Huia.AspNetCore.OpenIddict;
using Huia.EntityFrameworkCore;
using Huia.EntityFrameworkCore.Entities;
using Huia.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;

namespace Huia.AspNetCore.Endpoints;

/// <summary>CRUD for users, clients and signing keys. See <see cref="AdminEndpoints"/>.</summary>
internal static partial class AdminEndpoints
{
    // ---------------------------------------------------------------------------------------------
    // Users
    // ---------------------------------------------------------------------------------------------

    private static async Task<IResult> GetUserAsync(HttpContext context, HuiaDbContext db, string id)
    {
        var user = await db.Set<HuiaUser>().IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id, context.RequestAborted);
        if (user is null)
        {
            return Results.NotFound();
        }

        var roles = (await RolesByUserAsync(db, [id], context.RequestAborted)).GetValueOrDefault(id, []);
        return Results.Ok(new UserDto(user.Id, user.TenantId, user.UserName, user.Email, user.EmailConfirmed,
            user.PhoneNumber, user.PhoneNumberConfirmed, user.LockoutEnabled, user.LockoutEnd, roles));
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
                $"/admin/users/{Uri.EscapeDataString(user.Id)}",
                new UserDto(user.Id, user.TenantId, user.UserName, user.Email, user.EmailConfirmed,
                    user.PhoneNumber, user.PhoneNumberConfirmed, user.LockoutEnabled, user.LockoutEnd,
                    [.. body.Roles ?? []]));
        });
    }

    private static async Task<IResult> UpdateUserAsync(HttpContext context, HuiaDbContext db, string id, UpdateUserRequest body)
    {
        var tenantId = await db.Set<HuiaUser>().IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Id == id).Select(u => u.TenantId).FirstOrDefaultAsync(context.RequestAborted);
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

            if (body.EmailConfirmed is { } emailConfirmed)
            {
                user.EmailConfirmed = emailConfirmed;
            }

            if (body.LockoutEnabled is { } lockoutEnabled)
            {
                user.LockoutEnabled = lockoutEnabled;
            }

            if (body.ClearLockout == true)
            {
                user.LockoutEnd = null;
                await userManager.ResetAccessFailedCountAsync(user);
            }
            else if (body.LockoutEnd is { } lockoutEnd)
            {
                user.LockoutEnd = lockoutEnd;
            }

            var result = await userManager.UpdateAsync(user);
            return result.Succeeded ? Results.NoContent() : IdentityProblem(result);
        });
    }

    private static async Task<IResult> DeleteUserAsync(HttpContext context, HuiaDbContext db, string id)
    {
        var tenantId = await db.Set<HuiaUser>().IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Id == id).Select(u => u.TenantId).FirstOrDefaultAsync(context.RequestAborted);
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

    /// <summary>Locks a user out until <see cref="LockUserRequest.Until"/> (indefinitely by default).</summary>
    private static async Task<IResult> LockUserAsync(HttpContext context, HuiaDbContext db, string id, LockUserRequest? body)
    {
        var tenantId = await db.Set<HuiaUser>().IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Id == id).Select(u => u.TenantId).FirstOrDefaultAsync(context.RequestAborted);
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

    /// <summary>Clears an existing lockout and resets the failed-access counter.</summary>
    private static async Task<IResult> UnlockUserAsync(HttpContext context, HuiaDbContext db, string id)
    {
        var tenantId = await db.Set<HuiaUser>().IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Id == id).Select(u => u.TenantId).FirstOrDefaultAsync(context.RequestAborted);
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

    /// <summary>Confirms an email-and-password account's email address without a confirmation link.</summary>
    private static async Task<IResult> VerifyEmailAsync(HttpContext context, HuiaDbContext db, string id)
    {
        var tenantId = await db.Set<HuiaUser>().IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Id == id).Select(u => u.TenantId).FirstOrDefaultAsync(context.RequestAborted);
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

    // ---------------------------------------------------------------------------------------------
    // Clients
    // ---------------------------------------------------------------------------------------------

    private static async Task<IResult> GetClientAsync(
        HttpContext context, HuiaDbContext db, IOpenIddictApplicationManager manager, string id)
    {
        var propertiesJson = await db.Set<OpenIddictEntityFrameworkCoreApplication>().AsNoTracking()
            .Where(a => a.Id == id).Select(a => a.Properties).FirstOrDefaultAsync(context.RequestAborted);
        var tenant = ExtractTenant(propertiesJson);
        if (tenant is null)
        {
            return Results.NotFound();
        }

        using (HuiaTenantScope.Enter(context.RequestServices, tenant))
        {
            var app = await manager.FindByIdAsync(id, context.RequestAborted);
            if (app is null)
            {
                return Results.NotFound();
            }

            var redirects = await manager.GetRedirectUrisAsync(app, context.RequestAborted);
            var permissions = await manager.GetPermissionsAsync(app, context.RequestAborted);

            return Results.Ok(new ClientDetailDto(
                await manager.GetIdAsync(app, context.RequestAborted),
                await manager.GetClientIdAsync(app, context.RequestAborted),
                await manager.GetDisplayNameAsync(app, context.RequestAborted),
                await manager.GetClientTypeAsync(app, context.RequestAborted),
                tenant,
                ExtractOrigin(propertiesJson),
                [.. redirects],
                [.. permissions]));
        }
    }

    private static async Task<IResult> CreateClientAsync(
        HttpContext context, IOpenIddictApplicationManager manager, HuiaOptions options, ClientWriteRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Tenant) || !options.Tenants.ContainsKey(body.Tenant))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["tenant"] = ["Unknown tenant."] });
        }

        if (!TryBuildClientDescriptor(body, out var descriptor, out var problem))
        {
            return problem;
        }

        using (HuiaTenantScope.Enter(context.RequestServices, body.Tenant))
        {
            if (await manager.FindByClientIdAsync(descriptor.ClientId, context.RequestAborted) is not null)
            {
                return Results.Conflict(new { message = $"A client '{descriptor.ClientId}' already exists." });
            }

            var appDescriptor = HuiaApplicationDescriptorMapper.ToDescriptor(
                body.Tenant, descriptor, HuiaConstants.Origins.Dynamic);
            await manager.CreateAsync(appDescriptor, context.RequestAborted);
        }

        return Results.Created($"/admin/clients/{Uri.EscapeDataString(descriptor.ClientId)}",
            new { body.Tenant, descriptor.ClientId });
    }

    private static async Task<IResult> UpdateClientAsync(
        HttpContext context, HuiaDbContext db, IOpenIddictApplicationManager manager, string id, ClientWriteRequest body)
    {
        var row = await db.Set<OpenIddictEntityFrameworkCoreApplication>().AsNoTracking()
            .Where(a => a.Id == id).Select(a => new { a.Properties, a.ClientId }).FirstOrDefaultAsync(context.RequestAborted);
        var tenant = ExtractTenant(row?.Properties);
        if (row is null || tenant is null)
        {
            return Results.NotFound();
        }

        if (ExtractOrigin(row.Properties) != HuiaConstants.Origins.Dynamic)
        {
            return CodeDefinedClientProblem();
        }

        if (!TryBuildClientDescriptor(body with { Tenant = tenant, ClientId = body.ClientId ?? row.ClientId }, out var descriptor, out var problem))
        {
            return problem;
        }

        using (HuiaTenantScope.Enter(context.RequestServices, tenant))
        {
            var app = await manager.FindByIdAsync(id, context.RequestAborted);
            if (app is null)
            {
                return Results.NotFound();
            }

            var appDescriptor = HuiaApplicationDescriptorMapper.ToDescriptor(
                tenant, descriptor, HuiaConstants.Origins.Dynamic);
            await manager.UpdateAsync(app, appDescriptor, context.RequestAborted);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> DeleteClientAsync(
        HttpContext context, HuiaDbContext db, IOpenIddictApplicationManager manager, string id)
    {
        var propertiesJson = await db.Set<OpenIddictEntityFrameworkCoreApplication>().AsNoTracking()
            .Where(a => a.Id == id).Select(a => a.Properties).FirstOrDefaultAsync(context.RequestAborted);
        var tenant = ExtractTenant(propertiesJson);
        if (tenant is null)
        {
            return Results.NotFound();
        }

        if (ExtractOrigin(propertiesJson) != HuiaConstants.Origins.Dynamic)
        {
            return CodeDefinedClientProblem();
        }

        using (HuiaTenantScope.Enter(context.RequestServices, tenant))
        {
            var app = await manager.FindByIdAsync(id, context.RequestAborted);
            if (app is null)
            {
                return Results.NotFound();
            }

            await manager.DeleteAsync(app, context.RequestAborted);
        }

        return Results.NoContent();
    }

    private static bool TryBuildClientDescriptor(ClientWriteRequest body, out HuiaClientDescriptor descriptor, out IResult problem)
    {
        descriptor = new HuiaClientDescriptor();

        if (!Enum.TryParse<ClientKind>(body.Kind, ignoreCase: true, out var kind) || !Enum.IsDefined(kind))
        {
            problem = Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["kind"] = [$"Must be one of: {string.Join(", ", Enum.GetNames<ClientKind>())}."],
            });
            return false;
        }

        descriptor.ClientId = body.ClientId ?? string.Empty;
        descriptor.ClientSecret = body.ClientSecret;
        descriptor.DisplayName = body.DisplayName;
        descriptor.Kind = kind;
        descriptor.RequirePkce = body.RequirePkce ?? false;
        descriptor.RequireConsent = body.RequireConsent ?? false;
        descriptor.RequiresPushedAuthorizationRequests = body.RequirePushedAuthorizationRequests ?? false;

        try
        {
            descriptor.ClientUri = ParseAbsoluteUri(body.ClientUri);
            descriptor.LogoUri = ParseAbsoluteUri(body.LogoUri);
            AddUris(descriptor.RedirectUris, body.RedirectUris);
            AddUris(descriptor.PostLogoutRedirectUris, body.PostLogoutRedirectUris);
            AddUris(descriptor.HomeUris, body.HomeUris);
        }
        catch (UriFormatException ex)
        {
            problem = Results.ValidationProblem(new Dictionary<string, string[]> { ["uri"] = [ex.Message] });
            return false;
        }

        foreach (var scope in body.Scopes ?? [])
        {
            descriptor.Scopes.Add(scope);
        }

        if (body.Token is { } token)
        {
            descriptor.Token = new TokenLifetimeOptions
            {
                AccessToken = token.AccessToken,
                IdentityToken = token.IdentityToken,
                RefreshToken = token.RefreshToken,
                AuthorizationCode = token.AuthorizationCode,
                DeviceCode = token.DeviceCode,
                UserCode = token.UserCode,
            };
        }

        try
        {
            descriptor.Validate();
        }
        catch (HuiaOptionsException ex)
        {
            problem = Results.ValidationProblem(new Dictionary<string, string[]> { ["client"] = [.. ex.Errors] });
            return false;
        }

        problem = Results.Empty;
        return true;

        static void AddUris(IList<Uri> target, IReadOnlyList<string>? source)
        {
            foreach (var value in source ?? [])
            {
                target.Add(new Uri(value, UriKind.Absolute));
            }
        }
    }

    private static Uri? ParseAbsoluteUri(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : new Uri(value, UriKind.Absolute);

    private static IResult CodeDefinedClientProblem()
        => Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "This client is defined in code and cannot be modified through the admin API.");

    // ---------------------------------------------------------------------------------------------
    // Signing keys
    // ---------------------------------------------------------------------------------------------

    private static async Task<IResult> GetKeyAsync(HttpContext context, HuiaDbContext db, string id)
    {
        var key = await db.SigningKeys.AsNoTracking().FirstOrDefaultAsync(k => k.Id == id, context.RequestAborted);
        return key is null ? Results.NotFound() : Results.Ok(ToKeyDetail(key));
    }

    private static async Task<IResult> CreateKeyAsync(
        HttpContext context, HuiaDbContext db, HuiaSigningKeyFactory factory, IHuiaKeyRing keyRing,
        HuiaOptions options, TimeProvider timeProvider, CreateKeyRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Tenant) || !options.Tenants.ContainsKey(body.Tenant))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["tenant"] = ["Unknown tenant."] });
        }

        var now = timeProvider.GetUtcNow();
        var activate = body.Activate ?? false;
        var status = activate ? HuiaSigningKeyStatus.Active : HuiaSigningKeyStatus.Pending;
        var key = factory.Create(body.Tenant, options.Keys, status, body.ActivateAt ?? now);

        if (activate)
        {
            var current = await db.SigningKeys
                .Where(k => k.TenantId == body.Tenant && k.Status == HuiaSigningKeyStatus.Active)
                .ToListAsync(context.RequestAborted);
            foreach (var demoted in current)
            {
                demoted.Status = HuiaSigningKeyStatus.Rotated;
                demoted.RotatedAt = now;
            }
        }

        db.SigningKeys.Add(key);
        await db.SaveChangesAsync(context.RequestAborted);
        await keyRing.InvalidateAsync(body.Tenant);

        return Results.Created($"/admin/keys/{key.Id}", ToKeyDetail(key));
    }

    private static async Task<IResult> RevokeKeyAsync(
        HttpContext context, HuiaDbContext db, IHuiaKeyRing keyRing, TimeProvider timeProvider, string id)
    {
        var key = await db.SigningKeys.FirstOrDefaultAsync(k => k.Id == id, context.RequestAborted);
        if (key is null)
        {
            return Results.NotFound();
        }

        if (key.Status == HuiaSigningKeyStatus.Retired)
        {
            return Results.NoContent();
        }

        if (key.Status == HuiaSigningKeyStatus.Active)
        {
            var replacements = await db.SigningKeys.CountAsync(
                k => k.TenantId == key.TenantId && k.Id != id
                     && (k.Status == HuiaSigningKeyStatus.Active || k.Status == HuiaSigningKeyStatus.Pending),
                context.RequestAborted);
            if (replacements == 0)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Cannot revoke the tenant's only active signing key. Create a replacement first.");
            }
        }

        key.Status = HuiaSigningKeyStatus.Retired;
        key.RetiredAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(context.RequestAborted);
        await keyRing.InvalidateAsync(key.TenantId);

        return Results.NoContent();
    }

    private static async Task<IResult> DeleteKeyAsync(
        HttpContext context, HuiaDbContext db, IHuiaKeyRing keyRing, string id)
    {
        var key = await db.SigningKeys.FirstOrDefaultAsync(k => k.Id == id, context.RequestAborted);
        if (key is null)
        {
            return Results.NotFound();
        }

        if (key.Status != HuiaSigningKeyStatus.Retired)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Only a retired key can be deleted. Revoke it first.");
        }

        db.SigningKeys.Remove(key);
        await db.SaveChangesAsync(context.RequestAborted);
        await keyRing.InvalidateAsync(key.TenantId);

        return Results.NoContent();
    }

    private static KeyDetailDto ToKeyDetail(HuiaSigningKey key) => new(
        key.Id, key.TenantId, key.KeyId, key.Algorithm, key.Status.ToString(),
        key.CreatedAt, key.ActivateAt, key.RotatedAt, key.RetiredAt);

    // ---------------------------------------------------------------------------------------------
    // Shared helpers
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Runs <paramref name="work"/> in a fresh DI scope entered into <paramref name="tenantId"/>, so a
    /// <c>HuiaDbContext</c> / <c>UserManager</c> resolved inside it is bound to that tenant (the context
    /// snapshots the tenant at construction).
    /// </summary>
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

    private sealed record ClientDetailDto(
        string? Id,
        string? ClientId,
        string? DisplayName,
        string? ClientType,
        string? Tenant,
        string Origin,
        string[] RedirectUris,
        string[] Permissions);

    private sealed record KeyDetailDto(
        string Id,
        string TenantId,
        string KeyId,
        string Algorithm,
        string Status,
        DateTimeOffset CreatedAt,
        DateTimeOffset ActivateAt,
        DateTimeOffset? RotatedAt,
        DateTimeOffset? RetiredAt);

    private sealed record CreateUserRequest(
        string Tenant,
        string? Email,
        string? PhoneNumber,
        string? FirstName,
        string? LastName,
        string? Password,
        bool? EmailConfirmed,
        string[]? Roles);

    private sealed record UpdateUserRequest(
        string? FirstName,
        string? LastName,
        bool? EmailConfirmed,
        bool? LockoutEnabled,
        DateTimeOffset? LockoutEnd,
        bool? ClearLockout);

    private sealed record LockUserRequest(DateTimeOffset? Until);

    private sealed record ClientTokenLifetimesDto(
        TimeSpan? AccessToken,
        TimeSpan? IdentityToken,
        TimeSpan? RefreshToken,
        TimeSpan? AuthorizationCode,
        TimeSpan? DeviceCode,
        TimeSpan? UserCode);

    private sealed record ClientWriteRequest(
        string Tenant,
        string? ClientId,
        string? ClientSecret,
        string? DisplayName,
        string? Kind,
        string[]? RedirectUris,
        string[]? PostLogoutRedirectUris,
        string[]? HomeUris,
        string? ClientUri,
        string? LogoUri,
        string[]? Scopes,
        bool? RequirePkce,
        bool? RequireConsent,
        bool? RequirePushedAuthorizationRequests,
        ClientTokenLifetimesDto? Token);

    private sealed record CreateKeyRequest(
        string Tenant,
        bool? Activate,
        DateTimeOffset? ActivateAt);
}
