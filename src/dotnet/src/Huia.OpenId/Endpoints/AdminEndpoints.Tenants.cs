using Huia.Multitenancy;
using Huia.Options;
using Huia.Stores;
using Huia.Tenants;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.OpenId.Endpoints;

/// <summary>
/// Tenant management. Tenants declared in code ("static") are listed and readable but never modified here;
/// tenants created through this API ("dynamic") are persisted in the tenant store and picked up immediately.
/// Secrets (client secrets, SMTP password, SMS credentials) are never returned, and omitting one on update keeps
/// the stored value.
/// </summary>
internal static partial class AdminEndpoints
{
    private static async Task<IResult> GetTenantAsync(
        HttpContext context, IHuiaTenantProvider tenants, HuiaOptions options, string id)
    {
        if (!tenants.TryGetValue(id, out var tenant))
        {
            return Results.NotFound();
        }

        var origin = TenantOrigin(options, id);
        long? version = null;
        if (origin == HuiaConstants.Origins.Dynamic
            && context.RequestServices.GetService<IHuiaTenantStore>() is { } store
            && await store.FindAsync(id, context.RequestAborted) is { } stored)
        {
            version = stored.Version;
            tenant = stored.Tenant;
        }

        return Results.Ok(new TenantDetailDto(id, origin, version, HuiaTenantDocument.From(tenant).RedactSecrets()));
    }

    private static async Task<IResult> CreateTenantAsync(
        HttpContext context, IHuiaTenantProvider tenants, HuiaOptions options, TenantWriteRequest body)
    {
        if (!TryGetPersistedTenantServices(context, out var store, out var refresher, out var unavailable))
        {
            return unavailable;
        }

        if (!HuiaTenantIds.IsValid(body.TenantId))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["tenantId"] = ["Must be 1-63 lower-case letters, digits or hyphens, starting with a letter or digit, and not a reserved route name."],
            });
        }

        if (tenants.ContainsKey(body.TenantId!))
        {
            return Results.Conflict(new { message = $"A tenant '{body.TenantId}' already exists." });
        }

        var saved = await TrySaveAsync(context, store, body.TenantId!, body.Document, existing: null);
        if (saved is not null)
        {
            return saved;
        }

        await refresher.RefreshAsync(context.RequestAborted);
        return Results.Created($"/admin/tenants/{Uri.EscapeDataString(body.TenantId!)}", new { tenantId = body.TenantId });
    }

    private static async Task<IResult> UpdateTenantAsync(
        HttpContext context, IHuiaTenantProvider tenants, HuiaOptions options, string id, TenantWriteRequest body)
    {
        if (!tenants.ContainsKey(id))
        {
            return Results.NotFound();
        }

        if (TenantOrigin(options, id) != HuiaConstants.Origins.Dynamic)
        {
            return CodeDefinedTenantProblem();
        }

        if (!TryGetPersistedTenantServices(context, out var store, out var refresher, out var unavailable))
        {
            return unavailable;
        }

        var existing = await store.FindAsync(id, context.RequestAborted);
        if (existing is null)
        {
            return Results.NotFound();
        }

        var saved = await TrySaveAsync(context, store, id, body.Document, HuiaTenantDocument.From(existing.Tenant));
        if (saved is not null)
        {
            return saved;
        }

        await refresher.RefreshAsync(context.RequestAborted);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteTenantAsync(
        HttpContext context, IHuiaTenantProvider tenants, HuiaOptions options, string id)
    {
        if (!tenants.ContainsKey(id))
        {
            return Results.NotFound();
        }

        if (TenantOrigin(options, id) != HuiaConstants.Origins.Dynamic)
        {
            return CodeDefinedTenantProblem();
        }

        if (!TryGetPersistedTenantServices(context, out var store, out var refresher, out var unavailable))
        {
            return unavailable;
        }

        if (!await store.DeleteAsync(id, context.RequestAborted))
        {
            return Results.NotFound();
        }

        await refresher.RefreshAsync(context.RequestAborted);
        return Results.NoContent();
    }

    /// <summary>Saves the document; returns a problem result on failure, or <c>null</c> on success.</summary>
    private static async Task<IResult?> TrySaveAsync(
        HttpContext context, IHuiaTenantStore store, string tenantId, HuiaTenantDocument? document,
        HuiaTenantDocument? existing)
    {
        if (document is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["document"] = ["Is required."] });
        }

        try
        {
            if (existing is not null)
            {
                document.PreserveSecretsFrom(existing);
            }

            await store.SaveAsync(tenantId, document.ToTenant(tenantId), context.RequestAborted);
            return null;
        }
        catch (HuiaOptionsException ex)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["document"] = [.. ex.Errors] });
        }
        catch (Exception ex) when (ex is FormatException or UriFormatException or InvalidOperationException
                                       or NotSupportedException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["document"] = [ex.Message] });
        }
    }

    private static bool TryGetPersistedTenantServices(
        HttpContext context, out IHuiaTenantStore store, out IHuiaTenantRefresher refresher, out IResult unavailable)
    {
        var services = context.RequestServices;
        var foundStore = services.GetService<IHuiaTenantStore>();
        var foundRefresher = services.GetService<IHuiaTenantRefresher>();

        if (foundStore is null || foundRefresher is null)
        {
            store = null!;
            refresher = null!;
            unavailable = Results.Problem(
                statusCode: StatusCodes.Status501NotImplemented,
                title: "Runtime tenants are not enabled. Register a store adapter and call AddPersistedTenants().");
            return false;
        }

        store = foundStore;
        refresher = foundRefresher;
        unavailable = Results.Empty;
        return true;
    }

    private static string TenantOrigin(HuiaOptions options, string tenantId) =>
        options.Tenants.ContainsKey(tenantId) ? HuiaConstants.Origins.Static : HuiaConstants.Origins.Dynamic;

    private static IResult CodeDefinedTenantProblem()
        => Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "This tenant is defined in code and cannot be modified through the admin API.");

    private sealed record TenantDetailDto(string TenantId, string Origin, long? Version, HuiaTenantDocument Document);

    private sealed record TenantWriteRequest(string? TenantId, HuiaTenantDocument? Document);
}
