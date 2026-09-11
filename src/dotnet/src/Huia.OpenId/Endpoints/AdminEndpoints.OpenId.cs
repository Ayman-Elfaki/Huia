using System.Text.Json;
using Huia.Keys;
using Huia.Multitenancy;
using Huia.OpenId.OpenIddict;
using Huia.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpenIddict.Abstractions;

namespace Huia.OpenId.Endpoints;

/// <summary>
/// OpenID Connect admin endpoints: client, custom scope, and signing key CRUD.
/// </summary>
internal static class AdminEndpointsOpenId
{
    public static RouteGroupBuilder MapHuiaOpenIdAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("admin");
        group.MapHuiaAdminEndpoints();

        group.MapGet("clients", ListClientsAsync);
        group.MapGet("clients/{id}", GetClientAsync);
        group.MapPost("clients", CreateClientAsync);
        group.MapPut("clients/{id}", UpdateClientAsync);
        group.MapDelete("clients/{id}", DeleteClientAsync);

        group.MapGet("scopes", ListScopesAsync);
        group.MapPost("scopes", CreateScopeAsync);
        group.MapPut("scopes/{name}", UpdateScopeAsync);
        group.MapDelete("scopes/{name}", DeleteScopeAsync);

        group.MapGet("keys", ListKeysAsync);
        group.MapGet("keys/{id}", GetKeyAsync);
        group.MapPost("keys", CreateKeyAsync);
        group.MapPost("keys/{id}/revoke", RevokeKeyAsync);
        group.MapDelete("keys/{id}", DeleteKeyAsync);

        return group;
    }

    // ---------------------------------------------------------------------------------------------
    // Clients
    // ---------------------------------------------------------------------------------------------

    private static async Task<IResult> ListClientsAsync(HttpContext context, IOpenIddictApplicationManager manager)
    {
        var tenant = context.Request.Query["tenant"].ToString();
        var clients = new List<ClientDto>();

        await foreach (var app in manager.ListAsync(cancellationToken: context.RequestAborted))
        {
            var properties = await manager.GetPropertiesAsync(app, context.RequestAborted);
            var appTenant = ExtractTenant(properties);
            if (!string.IsNullOrEmpty(tenant) && !string.Equals(appTenant, tenant, StringComparison.Ordinal))
            {
                continue;
            }

            clients.Add(new ClientDto(
                await manager.GetIdAsync(app, context.RequestAborted),
                await manager.GetClientIdAsync(app, context.RequestAborted),
                await manager.GetDisplayNameAsync(app, context.RequestAborted),
                await manager.GetClientTypeAsync(app, context.RequestAborted),
                appTenant,
                ExtractOrigin(properties)));
        }

        return Results.Ok(new { data = clients });
    }

    private static async Task<IResult> GetClientAsync(HttpContext context, IOpenIddictApplicationManager manager, string id)
    {
        var app = await manager.FindByIdAsync(id, context.RequestAborted);
        if (app is null)
        {
            return Results.NotFound();
        }

        var properties = await manager.GetPropertiesAsync(app, context.RequestAborted);
        var tenant = ExtractTenant(properties);
        var redirects = await manager.GetRedirectUrisAsync(app, context.RequestAborted);
        var permissions = await manager.GetPermissionsAsync(app, context.RequestAborted);

        return Results.Ok(new ClientDetailDto(
            await manager.GetIdAsync(app, context.RequestAborted),
            await manager.GetClientIdAsync(app, context.RequestAborted),
            await manager.GetDisplayNameAsync(app, context.RequestAborted),
            await manager.GetClientTypeAsync(app, context.RequestAborted),
            tenant,
            ExtractOrigin(properties),
            [.. redirects],
            [.. permissions]));
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
                body.Tenant, descriptor, HuiaOpenIdConstants.Origins.Dynamic);
            await manager.CreateAsync(appDescriptor, context.RequestAborted);
        }

        return Results.Created($"/admin/clients/{Uri.EscapeDataString(descriptor.ClientId)}",
            new { body.Tenant, descriptor.ClientId });
    }

    private static async Task<IResult> UpdateClientAsync(
        HttpContext context, IOpenIddictApplicationManager manager, string id, ClientWriteRequest body)
    {
        var app = await manager.FindByIdAsync(id, context.RequestAborted);
        if (app is null)
        {
            return Results.NotFound();
        }

        var properties = await manager.GetPropertiesAsync(app, context.RequestAborted);
        var tenant = ExtractTenant(properties);
        if (tenant is null)
        {
            return Results.NotFound();
        }

        if (ExtractOrigin(properties) != HuiaOpenIdConstants.Origins.Dynamic)
        {
            return CodeDefinedClientProblem();
        }

        var currentClientId = await manager.GetClientIdAsync(app, context.RequestAborted);
        if (!TryBuildClientDescriptor(body with { Tenant = tenant, ClientId = body.ClientId ?? currentClientId }, out var descriptor, out var problem))
        {
            return problem;
        }

        using (HuiaTenantScope.Enter(context.RequestServices, tenant))
        {
            var appDescriptor = HuiaApplicationDescriptorMapper.ToDescriptor(
                tenant, descriptor, HuiaOpenIdConstants.Origins.Dynamic);
            await manager.UpdateAsync(app, appDescriptor, context.RequestAborted);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> DeleteClientAsync(
        HttpContext context, IOpenIddictApplicationManager manager, string id)
    {
        var app = await manager.FindByIdAsync(id, context.RequestAborted);
        if (app is null)
        {
            return Results.NotFound();
        }

        var properties = await manager.GetPropertiesAsync(app, context.RequestAborted);
        if (ExtractOrigin(properties) != HuiaOpenIdConstants.Origins.Dynamic)
        {
            return CodeDefinedClientProblem();
        }

        var tenant = ExtractTenant(properties);
        if (tenant is null)
        {
            return Results.NotFound();
        }

        using (HuiaTenantScope.Enter(context.RequestServices, tenant))
        {
            await manager.DeleteAsync(app, context.RequestAborted);
        }

        return Results.NoContent();
    }

    // ---------------------------------------------------------------------------------------------
    // Scopes
    // ---------------------------------------------------------------------------------------------

    private static async Task<IResult> ListScopesAsync(HttpContext context, IOpenIddictScopeManager manager)
    {
        var tenant = context.Request.Query["tenant"].ToString();
        var scopes = new List<ScopeDto>();

        await foreach (var scope in manager.ListAsync(cancellationToken: context.RequestAborted))
        {
            var properties = await manager.GetPropertiesAsync(scope, context.RequestAborted);
            var scopeTenant = ExtractTenant(properties);
            if (!string.IsNullOrEmpty(tenant) && !string.Equals(scopeTenant, tenant, StringComparison.Ordinal))
            {
                continue;
            }

            scopes.Add(new ScopeDto(
                await manager.GetIdAsync(scope, context.RequestAborted),
                await manager.GetNameAsync(scope, context.RequestAborted),
                await manager.GetDisplayNameAsync(scope, context.RequestAborted),
                await manager.GetDescriptionAsync(scope, context.RequestAborted),
                scopeTenant,
                ExtractOrigin(properties)));
        }

        return Results.Ok(scopes);
    }

    private static async Task<IResult> CreateScopeAsync(
        HttpContext context, IOpenIddictScopeManager manager, HuiaOptions options, CreateScopeRequest body)
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
                return Results.Conflict(new { message = $"Scope '{body.Name}' already exists for tenant '{body.Tenant}'." });
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

            descriptor.Properties[HuiaOpenIdConstants.ApplicationProperties.Tenant] =
                JsonSerializer.SerializeToElement(body.Tenant);
            descriptor.Properties[HuiaOpenIdConstants.ApplicationProperties.Origin] =
                JsonSerializer.SerializeToElement(HuiaOpenIdConstants.Origins.Dynamic);
            await manager.CreateAsync(descriptor, context.RequestAborted);
        }

        return Results.Created($"/admin/scopes/{body.Name}?tenant={body.Tenant}", new { body.Tenant, body.Name });
    }

    private static async Task<IResult> UpdateScopeAsync(
        HttpContext context, string name, IOpenIddictScopeManager manager, UpdateScopeRequest body)
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
        HttpContext context, string name, IOpenIddictScopeManager manager)
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

    // ---------------------------------------------------------------------------------------------
    // Keys
    // ---------------------------------------------------------------------------------------------

    private static async Task<IResult> ListKeysAsync(
        HttpContext context, IHuiaSigningKeyStore keyStore)
    {
        var tenant = context.Request.Query["tenant"].ToString();
        var allKeys = await keyStore.GetAllKeysAsync(context.RequestAborted);

        var keys = allKeys
            .Where(k => string.IsNullOrEmpty(tenant) || string.Equals(k.TenantId, tenant, StringComparison.Ordinal))
            .Select(k => new KeyDto(k.Id, k.TenantId, k.KeyId, k.Algorithm, k.Status.ToString(), k.CreatedAt))
            .ToList();

        return Results.Ok(new { data = keys });
    }

    private static async Task<IResult> GetKeyAsync(
        HttpContext context, IHuiaSigningKeyStore keyStore, string id)
    {
        var allKeys = await keyStore.GetAllKeysAsync(context.RequestAborted);
        var key = allKeys.FirstOrDefault(k => k.Id == id);
        return key is null ? Results.NotFound() : Results.Ok(ToKeyDetail(key));
    }

    private static async Task<IResult> CreateKeyAsync(
        HttpContext context, IHuiaSigningKeyStore keyStore, HuiaSigningKeyFactory factory, IHuiaKeyRing keyRing,
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
            var current = await keyStore.GetPublishedKeysAsync(body.Tenant, context.RequestAborted);
            foreach (var demoted in current.Where(k => k.Status == HuiaSigningKeyStatus.Active))
            {
                await keyStore.UpdateKeyStatusAsync(demoted.Id, HuiaSigningKeyStatus.Rotated, now, context.RequestAborted);
            }
        }

        await keyStore.AddKeyAsync(key, context.RequestAborted);
        await keyRing.InvalidateAsync(body.Tenant);

        return Results.Created($"/admin/keys/{key.Id}", ToKeyDetail(key));
    }

    private static async Task<IResult> RevokeKeyAsync(
        HttpContext context, IHuiaSigningKeyStore keyStore, IHuiaKeyRing keyRing, TimeProvider timeProvider, string id)
    {
        var allKeys = await keyStore.GetAllKeysAsync(context.RequestAborted);
        var key = allKeys.FirstOrDefault(k => k.Id == id);
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
            var replacements = allKeys.Count(k => k.TenantId == key.TenantId && k.Id != id &&
                (k.Status == HuiaSigningKeyStatus.Active || k.Status == HuiaSigningKeyStatus.Pending));
            if (replacements == 0)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Cannot revoke the tenant's only active signing key. Create a replacement first.");
            }
        }

        await keyStore.UpdateKeyStatusAsync(key.Id, HuiaSigningKeyStatus.Retired, timeProvider.GetUtcNow(), context.RequestAborted);
        await keyRing.InvalidateAsync(key.TenantId);

        return Results.NoContent();
    }

    private static async Task<IResult> DeleteKeyAsync(
        HttpContext context, IHuiaSigningKeyStore keyStore, IHuiaKeyRing keyRing, string id)
    {
        var allKeys = await keyStore.GetAllKeysAsync(context.RequestAborted);
        var key = allKeys.FirstOrDefault(k => k.Id == id);
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

        await keyStore.DeleteKeysAsync([key.Id], context.RequestAborted);
        await keyRing.InvalidateAsync(key.TenantId);

        return Results.NoContent();
    }

    private static KeyDetailDto ToKeyDetail(HuiaSigningKeyRecord key) => new(
        key.Id, key.TenantId, key.KeyId, key.Algorithm, key.Status.ToString(),
        key.CreatedAt, key.ActivateAt, key.RotatedAt, key.RetiredAt);

    private static string? ExtractTenant(System.Collections.Immutable.ImmutableDictionary<string, JsonElement> properties)
        => properties.TryGetValue(HuiaOpenIdConstants.ApplicationProperties.Tenant, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string ExtractOrigin(System.Collections.Immutable.ImmutableDictionary<string, JsonElement> properties)
        => properties.TryGetValue(HuiaOpenIdConstants.ApplicationProperties.Origin, out var value) &&
           value.ValueKind == JsonValueKind.String &&
           string.Equals(value.GetString(), HuiaOpenIdConstants.Origins.Dynamic, StringComparison.Ordinal)
            ? HuiaOpenIdConstants.Origins.Dynamic
            : HuiaOpenIdConstants.Origins.Static;

    private static async ValueTask<bool> IsCodeDefinedAsync(
        IOpenIddictScopeManager manager, object scope, CancellationToken cancellationToken)
    {
        var properties = await manager.GetPropertiesAsync(scope, cancellationToken);
        return !(properties.TryGetValue(HuiaOpenIdConstants.ApplicationProperties.Origin, out var value)
                 && value.ValueKind == JsonValueKind.String
                 && string.Equals(value.GetString(), HuiaOpenIdConstants.Origins.Dynamic, StringComparison.Ordinal));
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

        problem = Results.Ok();
        return true;
    }

    private static void AddUris(IList<Uri> destination, string[]? sources)
    {
        if (sources is null) return;
        foreach (var s in sources)
        {
            if (!string.IsNullOrWhiteSpace(s))
            {
                destination.Add(new Uri(s.Trim(), UriKind.Absolute));
            }
        }
    }

    private static Uri? ParseAbsoluteUri(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : new Uri(value.Trim(), UriKind.Absolute);

    private static IResult CodeDefinedClientProblem()
        => Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "This client is defined in code and cannot be modified through the admin API.");

    private static IResult CodeDefinedScopeProblem()
        => Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "This scope is defined in code and cannot be modified through the admin API.");

    private sealed record ClientDto(string? Id, string? ClientId, string? DisplayName, string? ClientType, string? Tenant, string Origin);
    private sealed record ClientDetailDto(string? Id, string? ClientId, string? DisplayName, string? ClientType, string? Tenant, string Origin, string[] RedirectUris, string[] Permissions);
    private sealed record ClientWriteRequest(string Tenant, string? ClientId, string? ClientSecret, string? DisplayName, string Kind, string? ClientUri, string? LogoUri, string[]? RedirectUris, string[]? PostLogoutRedirectUris, string[]? HomeUris, string[]? Scopes, bool? RequirePkce, bool? RequireConsent, bool? RequirePushedAuthorizationRequests);
    private sealed record ScopeDto(string? Id, string? Name, string? DisplayName, string? Description, string? Tenant, string Origin);
    private sealed record CreateScopeRequest(string Tenant, string Name, string? DisplayName, string? Description, string[]? Resources);
    private sealed record UpdateScopeRequest(string Tenant, string? DisplayName, string? Description, string[]? Resources);
    private sealed record KeyDto(string Id, string TenantId, string KeyId, string Algorithm, string Status, DateTimeOffset CreatedAt);
    private sealed record KeyDetailDto(string Id, string TenantId, string KeyId, string Algorithm, string Status, DateTimeOffset CreatedAt, DateTimeOffset ActivateAt, DateTimeOffset? RotatedAt, DateTimeOffset? RetiredAt);
    private sealed class CreateKeyRequest
    {
        public string Tenant { get; set; } = string.Empty;
        public bool? Activate { get; set; }
        public DateTimeOffset? ActivateAt { get; set; }
    }
}
