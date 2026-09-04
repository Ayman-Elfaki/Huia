using System.Text.Json;
using Huia.AspNetCore.Multitenancy;
using Huia.EntityFrameworkCore;
using Huia.EntityFrameworkCore.Entities;
using Huia.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MR.AspNetCore.Pagination;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;

namespace Huia.AspNetCore.Endpoints;

/// <summary>
/// Administrative endpoints (<c>/admin/*</c>). The group is returned <em>without</em> an
/// authorization policy — the host attaches its own (for example
/// <c>.RequireAuthorization(p =&gt; p.RequireTenants("master").RequireRole(HuiaConstants.Roles.Administrator))</c>).
/// Lists use keyset (cursor) pagination via <c>MR.AspNetCore.Pagination</c>, never offset. Users,
/// clients and custom scopes have full CRUD; signing keys can be created and revoked. Everything
/// created through this API is stamped <c>huia:origin = dynamic</c>; code-seeded ("static") clients
/// and scopes are read-only (a mutating call returns 409).
/// </summary>
internal static partial class AdminEndpoints
{
    public static RouteGroupBuilder MapHuiaAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("admin");

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

        group.MapGet("clients", ListClientsAsync);
        group.MapGet("clients/{id}", GetClientAsync);
        group.MapPost("clients", CreateClientAsync);
        group.MapPut("clients/{id}", UpdateClientAsync);
        group.MapDelete("clients/{id}", DeleteClientAsync);

        group.MapGet("keys", ListKeysAsync);
        group.MapGet("keys/{id}", GetKeyAsync);
        group.MapPost("keys", CreateKeyAsync);
        group.MapPost("keys/{id}/revoke", RevokeKeyAsync);
        group.MapDelete("keys/{id}", DeleteKeyAsync);

        group.MapGet("scopes", ListScopesAsync);
        group.MapPost("scopes", CreateScopeAsync);
        group.MapPut("scopes/{name}", UpdateScopeAsync);
        group.MapDelete("scopes/{name}", DeleteScopeAsync);

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
                kvp.Value.Authentication.IsExternalLoginEnabled,
                kvp.Value.Clients.Count))
            .OrderBy(t => t.TenantId, StringComparer.Ordinal)
            .ToList();

        return Results.Ok(tenants);
    }

    private static async Task<IResult> ListUsersAsync(HttpContext context, HuiaDbContext db,
        IPaginationService pagination)
    {
        var tenant = context.Request.Query["tenant"].ToString();

        // The admin console lists users across every tenant, so bypass HuiaDbContext's per-tenant
        // query filter and narrow explicitly when a ?tenant= is supplied.
        var source = db.Set<HuiaUser>().IgnoreQueryFilters().AsNoTracking();
        if (!string.IsNullOrEmpty(tenant))
        {
            source = source.Where(u => u.TenantId == tenant);
        }

        var result = await pagination.KeysetPaginateAsync(
            source,
            builder => builder.Ascending(u => u.Id),
            async id => await db.Set<HuiaUser>().IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Id == id, context.RequestAborted),
            users => users.Select(u => new UserDto(u.Id, u.TenantId, u.UserName, u.Email, u.EmailConfirmed,
                u.PhoneNumber, u.PhoneNumberConfirmed, u.LockoutEnabled, u.LockoutEnd, Array.Empty<string>())),
            ReadQuery(context));

        var byUser = await RolesByUserAsync(db, [.. result.Data.Select(d => d.Id)], context.RequestAborted);
        var data = result.Data.Select(d => d with { Roles = byUser.GetValueOrDefault(d.Id, []) }).ToList();

        return Results.Ok(new { data, result.HasNext, result.HasPrevious });
    }

    /// <summary>Maps a set of user ids to their role names in one query (tenant filter bypassed).</summary>
    private static async Task<Dictionary<string, string[]>> RolesByUserAsync(
        HuiaDbContext db, IReadOnlyCollection<string> userIds, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return [];
        }

        var rows = await db.Set<Microsoft.AspNetCore.Identity.IdentityUserRole<string>>().IgnoreQueryFilters().AsNoTracking()
            .Where(ur => userIds.Contains(ur.UserId))
            .Join(db.Set<HuiaRole>().IgnoreQueryFilters().AsNoTracking(),
                ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .ToListAsync(cancellationToken);

        return rows.GroupBy(x => x.UserId).ToDictionary(g => g.Key, g => g.Select(x => x.Name!).ToArray());
    }

    private static async Task<IResult> ListClientsAsync(HttpContext context, HuiaDbContext db,
        IPaginationService pagination)
    {
        var result = await pagination.KeysetPaginateAsync(
            db.Set<OpenIddictEntityFrameworkCoreApplication>().AsNoTracking(),
            builder => builder.Ascending(a => a.Id!),
            async id => await db.Set<OpenIddictEntityFrameworkCoreApplication>()
                .FindAsync([id], context.RequestAborted),
            apps => apps.Select(a => new ClientDto(a.Id, a.ClientId, a.DisplayName, a.ClientType,
                ExtractTenant(a.Properties), ExtractOrigin(a.Properties))),
            ReadQuery(context));

        return Results.Ok(result);
    }

    private static async Task<IResult> ListKeysAsync(HttpContext context, HuiaDbContext db,
        IPaginationService pagination)
    {
        var tenant = context.Request.Query["tenant"].ToString();
        var source = db.SigningKeys.AsNoTracking();
        if (!string.IsNullOrEmpty(tenant))
        {
            source = source.Where(k => k.TenantId == tenant);
        }

        var result = await pagination.KeysetPaginateAsync(
            source,
            builder => builder.Ascending(k => k.Id),
            async id => await db.SigningKeys.FindAsync([id], context.RequestAborted),
            keys => keys.Select(k =>
                new KeyDto(k.Id, k.TenantId, k.KeyId, k.Algorithm, k.Status.ToString(), k.CreatedAt)),
            ReadQuery(context));

        return Results.Ok(result);
    }

    private static async Task<IResult> ListScopesAsync(HttpContext context, HuiaDbContext db)
    {
        var tenant = context.Request.Query["tenant"].ToString();

        var rows = await db.Set<OpenIddictEntityFrameworkCoreScope>().AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => new { s.Id, s.Name, s.DisplayName, s.Description, s.Properties })
            .ToListAsync(context.RequestAborted);

        var scopes = rows
            .Select(s => new ScopeDto(s.Id, s.Name, s.DisplayName, s.Description, ExtractTenant(s.Properties),
                ExtractOrigin(s.Properties)))
            .Where(s => string.IsNullOrEmpty(tenant) || string.Equals(s.Tenant, tenant, StringComparison.Ordinal))
            .ToList();

        return Results.Ok(scopes);
    }

    private static async Task<IResult> CreateScopeAsync(
        HttpContext context,
        IOpenIddictScopeManager manager,
        HuiaOptions options,
        CreateScopeRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Tenant) || !options.Tenants.ContainsKey(body.Tenant))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["tenant"] = ["Unknown tenant."] });
        }

        if (!HuiaScopeDescriptor.IsValidScopeName(body.Name))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["name"] = ["A scope name must contain only lower-case letters, digits, ':', '_' and '-'."],
            });
        }

        using (HuiaTenantScope.Enter(context.RequestServices, body.Tenant))
        {
            if (await manager.FindByNameAsync(body.Name, context.RequestAborted) is not null)
            {
                return Results.Conflict(new
                    { message = $"Scope '{body.Name}' already exists for tenant '{body.Tenant}'." });
            }

            var descriptor = new OpenIddictScopeDescriptor
            {
                Name = body.Name,
                DisplayName = body.DisplayName ?? body.Name,
                Description = body.Description,
            };

            foreach (var resource in body.Resources ?? [])
            {
                descriptor.Resources.Add(resource);
            }

            descriptor.Properties[HuiaConstants.ApplicationProperties.Tenant] =
                JsonSerializer.SerializeToElement(body.Tenant);
            descriptor.Properties[HuiaConstants.ApplicationProperties.Origin] =
                JsonSerializer.SerializeToElement(HuiaConstants.Origins.Dynamic);
            await manager.CreateAsync(descriptor, context.RequestAborted);
        }

        return Results.Created($"/admin/scopes/{body.Name}?tenant={body.Tenant}", new { body.Tenant, body.Name });
    }

    private static async Task<IResult> UpdateScopeAsync(
        HttpContext context,
        string name,
        IOpenIddictScopeManager manager,
        UpdateScopeRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Tenant))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["tenant"] = ["Is required."] });
        }

        using (HuiaTenantScope.Enter(context.RequestServices, body.Tenant))
        {
            var scope = await manager.FindByNameAsync(name, context.RequestAborted);
            if (scope is null)
            {
                return Results.NotFound();
            }

            if (await IsCodeDefinedAsync(manager, scope, context.RequestAborted))
            {
                return CodeDefinedScopeProblem();
            }

            var descriptor = new OpenIddictScopeDescriptor();
            await manager.PopulateAsync(descriptor, scope, context.RequestAborted);
            descriptor.DisplayName = body.DisplayName ?? descriptor.DisplayName;
            descriptor.Description = body.Description ?? descriptor.Description;
            if (body.Resources is not null)
            {
                descriptor.Resources.Clear();
                foreach (var resource in body.Resources)
                {
                    descriptor.Resources.Add(resource);
                }
            }

            await manager.UpdateAsync(scope, descriptor, context.RequestAborted);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> DeleteScopeAsync(
        HttpContext context,
        string name,
        IOpenIddictScopeManager manager)
    {
        var tenant = context.Request.Query["tenant"].ToString();
        if (string.IsNullOrEmpty(tenant))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["tenant"] = ["The 'tenant' query parameter is required."],
            });
        }

        using (HuiaTenantScope.Enter(context.RequestServices, tenant))
        {
            var scope = await manager.FindByNameAsync(name, context.RequestAborted);
            if (scope is null)
            {
                return Results.NotFound();
            }

            if (await IsCodeDefinedAsync(manager, scope, context.RequestAborted))
            {
                return CodeDefinedScopeProblem();
            }

            await manager.DeleteAsync(scope, context.RequestAborted);
        }

        return Results.NoContent();
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

    private static string? ExtractTenant(string? propertiesJson)
        => ExtractProperty(propertiesJson, HuiaConstants.ApplicationProperties.Tenant);

    /// <summary>
    /// Reads the <c>huia:origin</c> marker off an OpenIddict entity's <c>Properties</c> JSON. Anything other
    /// than an explicit <see cref="HuiaConstants.Origins.Dynamic"/> — including a missing marker on rows seeded
    /// before the marker existed — is reported as <see cref="HuiaConstants.Origins.Static"/> (read-only).
    /// </summary>
    private static string ExtractOrigin(string? propertiesJson)
        => string.Equals(
            ExtractProperty(propertiesJson, HuiaConstants.ApplicationProperties.Origin),
            HuiaConstants.Origins.Dynamic,
            StringComparison.Ordinal)
            ? HuiaConstants.Origins.Dynamic
            : HuiaConstants.Origins.Static;

    private static string? ExtractProperty(string? propertiesJson, string key)
    {
        if (string.IsNullOrEmpty(propertiesJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(propertiesJson);
            return document.RootElement.TryGetProperty(key, out var value)
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// A scope is "code-defined" (and therefore read-only over the admin API) unless it carries an explicit
    /// <c>huia:origin = dynamic</c> marker. Callers must already be inside the owning tenant's DI scope.
    /// </summary>
    private static async ValueTask<bool> IsCodeDefinedAsync(
        IOpenIddictScopeManager manager, object scope, CancellationToken cancellationToken)
    {
        var properties = await manager.GetPropertiesAsync(scope, cancellationToken);
        return !(properties.TryGetValue(HuiaConstants.ApplicationProperties.Origin, out var value)
                 && value.ValueKind == JsonValueKind.String
                 && string.Equals(value.GetString(), HuiaConstants.Origins.Dynamic, StringComparison.Ordinal));
    }

    private static IResult CodeDefinedScopeProblem()
        => Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "This scope is defined in code and cannot be modified through the admin API.");

    private sealed record TenantDto(
        string TenantId,
        string DisplayName,
        bool PasswordEnabled,
        bool PhoneLoginEnabled,
        bool ExternalLoginEnabled,
        int ClientCount);

    private sealed record UserDto(
        string Id,
        string TenantId,
        string? UserName,
        string? Email,
        bool EmailConfirmed,
        string? PhoneNumber,
        bool PhoneNumberConfirmed,
        bool LockoutEnabled,
        DateTimeOffset? LockoutEnd,
        string[] Roles);

    private sealed record ClientDto(
        string? Id,
        string? ClientId,
        string? DisplayName,
        string? ClientType,
        string? Tenant,
        string Origin);

    private sealed record KeyDto(
        string Id,
        string TenantId,
        string KeyId,
        string Algorithm,
        string Status,
        DateTimeOffset CreatedAt);

    private sealed record ScopeDto(
        string? Id,
        string? Name,
        string? DisplayName,
        string? Description,
        string? Tenant,
        string Origin);

    private sealed record CreateScopeRequest(
        string Tenant,
        string Name,
        string? DisplayName,
        string? Description,
        string[]? Resources);

    private sealed record UpdateScopeRequest(
        string Tenant,
        string? DisplayName,
        string? Description,
        string[]? Resources);
}
