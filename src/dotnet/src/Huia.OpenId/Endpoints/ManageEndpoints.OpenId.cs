using System.Security.Claims;
using Finbuckle.MultiTenant.Abstractions;
using Huia.Identity;
using Huia.Multitenancy;
using Huia.OpenId.Options;
using Huia.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;

namespace Huia.OpenId.Endpoints;

/// <summary>
/// OpenID / passkey / external login extensions for the self-service <c>/manage/*</c> API.
/// </summary>
internal static class ManageEndpointsOpenId
{
    public static RouteGroupBuilder MapHuiaOpenIdManageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("manage");
        group.RequireAuthorization(HuiaConstants.Policies.Api);

        group.MapGet("external-logins", GetExternalLoginsAsync);
        group.MapDelete("external-logins/{provider}/{providerKey}", RemoveExternalLoginAsync);

        PasskeyEndpoints.MapHuiaManagePasskeyEndpoints(group);

        return group;
    }

    private static async Task<IResult> GetExternalLoginsAsync(
        HttpContext context, HuiaUserManager userManager, IMultiTenantContextAccessor tenantAccessor, HuiaOptions huiaOptions)
    {
        var user = await ResolveUserAsync(context, userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var logins = await userManager.GetLoginsAsync(user);
        var linkedIds = logins.Select(l => l.LoginProvider).ToHashSet(StringComparer.Ordinal);
        var tenantId = tenantAccessor.RequireCurrentTenantId();
        var configured = huiaOptions.Tenants.TryGetValue(tenantId, out var tenant)
            ? tenant.GetHuiaOpenId()?.External?.Providers ?? []
            : [];

        return Results.Ok(new ExternalLoginsDto(
            [.. logins.Select(l => new ExternalLoginDto(l.LoginProvider, l.ProviderKey, ShortProviderName(l.LoginProvider), l.ProviderDisplayName))],
            [.. configured.Where(p => !linkedIds.Contains($"{tenantId}:{p.Name}")).Select(p => p.Name)],
            await userManager.CanRemoveExternalLoginAsync(user)));
    }

    private static async Task<IResult> RemoveExternalLoginAsync(
        HttpContext context, HuiaUserManager userManager, string provider, string providerKey)
    {
        var user = await ResolveUserAsync(context, userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        if (!await userManager.CanRemoveExternalLoginAsync(user))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["externalLogin"] = ["You cannot remove your only sign-in method."],
            });
        }

        var result = await userManager.RemoveLoginAsync(user, provider, providerKey);
        return result.Succeeded ? Results.NoContent() : Problem(result);
    }

    private static string ShortProviderName(string loginProvider) =>
        loginProvider.Contains(':', StringComparison.Ordinal)
            ? loginProvider[(loginProvider.LastIndexOf(':') + 1)..]
            : loginProvider;

    private static async Task<HuiaUser?> ResolveUserAsync(HttpContext context, HuiaUserManager userManager)
    {
        var principal = context.User;
        var subject = principal.FindFirstValue("sub")
                      ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return subject is null ? null : await userManager.FindByIdAsync(subject);
    }

    private static IResult Problem(IdentityResult result) =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["identity"] = result.Errors.Select(e => e.Description).ToArray(),
        });

    private sealed record ExternalLoginDto(string Provider, string ProviderKey, string ShortName, string? DisplayName);

    private sealed record ExternalLoginsDto(ExternalLoginDto[] Logins, string[] Available, bool CanRemoveAny);
}
