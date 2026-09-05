using System.Text.Json;
using Finbuckle.MultiTenant.Abstractions;
using Huia.AspNetCore.Flows;
using Huia.AspNetCore.Identity;
using Huia.EntityFrameworkCore.Entities;
using Huia.EntityFrameworkCore.Multitenancy;
using Huia.Events;
using Huia.Options;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;

namespace Huia.AspNetCore.Endpoints;

/// <summary>
/// Passkey (WebAuthn / FIDO2) endpoints. The anonymous discoverable sign-in routes live under
/// <c>identity/account/passkey/*</c> and are mapped alongside the account UI; the credential-management
/// routes are added to the token-protected <c>manage</c> group. Every route 404s when the resolved
/// tenant has not called <c>UsePasskeyLogin()</c>.
/// </summary>
internal static class PasskeyEndpoints
{
    /// <summary>Maps the anonymous passkey ceremony and sign-in endpoints.</summary>
    /// <param name="endpoints">The endpoint route builder (already tenant-rebased by <c>UseHuia()</c>).</param>
    /// <returns>The passkey route group.</returns>
    public static RouteGroupBuilder MapHuiaPasskeyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("identity/account/passkey");

        group.MapPost("assertion-options", AssertionOptionsAsync).WithName("huia.passkey.assertion-options");
        group.MapPost("assertion", AssertionAsync).WithName("huia.passkey.assertion");

        return group;
    }

    /// <summary>Adds the credential-management routes to the token-protected <c>manage</c> group.</summary>
    /// <param name="manage">The <c>manage</c> route group from <c>ManageEndpoints</c>.</param>
    public static void MapHuiaManagePasskeyEndpoints(RouteGroupBuilder manage)
    {
        var group = manage.MapGroup("passkeys");

        group.MapPost("creation-options", CreationOptionsAsync);
        group.MapGet("", ListAsync);
        group.MapPost("", RegisterAsync);
        group.MapPatch("{id}", RenameAsync);
        group.MapDelete("{id}", RemoveAsync);
    }

    // ---- anonymous: discoverable primary sign-in --------------------------------------------------

    private static async Task<IResult> AssertionOptionsAsync(
        HttpContext context, HuiaSignInManager signInManager, IAntiforgery antiforgery,
        IMultiTenantContextAccessor tenantAccessor, HuiaOptions options)
    {
        if (!PasskeyEnabled(options, tenantAccessor))
        {
            return Results.NotFound();
        }

        if (!await ValidateAntiforgeryAsync(context, antiforgery))
        {
            return Results.BadRequest(new { error = "antiforgery" });
        }

        var json = await signInManager.MakePasskeyRequestOptionsAsync(user: null);
        return Results.Content(json, "application/json");
    }

    private static async Task<IResult> AssertionAsync(
        HttpContext context, HuiaSignInManager signInManager, IAntiforgery antiforgery,
        IMultiTenantContextAccessor tenantAccessor, IReturnUrlProtector returnUrlProtector,
        IHuiaEventPublisher events, TimeProvider timeProvider, HuiaOptions options, PasskeyAssertionRequest body)
    {
        if (!PasskeyEnabled(options, tenantAccessor))
        {
            return Results.NotFound();
        }

        if (!await ValidateAntiforgeryAsync(context, antiforgery))
        {
            return Results.BadRequest(new { error = "antiforgery" });
        }

        SignInResult result;
        HuiaUser? user;
        try
        {
            (result, user) = await signInManager.PasskeyPrimarySignInAsync(body.Credential.GetRawText(), body.RememberMe);
        }
        catch (InvalidOperationException)
        {
            // No assertion ceremony is in progress (options were never requested, or the state cookie expired).
            return Results.BadRequest(new { error = "no_ceremony_in_progress" });
        }

        if (!result.Succeeded || user is null)
        {
            return Results.BadRequest(new { error = result.IsLockedOut ? "locked_out" : "assertion_failed" });
        }

        var tenantId = tenantAccessor.RequireCurrentTenantId();
        await events.PublishAsync(new UserLoggedInEvent(
            tenantId, user.Id, HuiaConstants.AuthenticationMethods.Passkey, null, timeProvider.GetUtcNow()));

        var returnUrl = returnUrlProtector.SanitizeReturnUrl(body.ReturnUrl, context);
        return Results.Ok(new { redirectUrl = ResolveRedirect(context, returnUrl, options, tenantId) });
    }

    // ---- token-protected: credential management ------------------------------------------------

    private static async Task<IResult> CreationOptionsAsync(
        HttpContext context, HuiaSignInManager signInManager, HuiaUserManager userManager,
        IMultiTenantContextAccessor tenantAccessor, HuiaOptions options)
    {
        if (!PasskeyEnabled(options, tenantAccessor))
        {
            return Results.NotFound();
        }

        var user = await ManageEndpoints.ResolveUserAsync(context, userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var entity = new PasskeyUserEntity
        {
            Id = user.Id,
            Name = user.UserName ?? user.Email ?? user.Id,
            DisplayName = $"{user.FirstName} {user.LastName}".Trim() is { Length: > 0 } name ? name : (user.UserName ?? user.Id),
        };

        var json = await signInManager.MakePasskeyCreationOptionsAsync(entity);
        return Results.Content(json, "application/json");
    }

    private static async Task<IResult> RegisterAsync(
        HttpContext context, HuiaUserManager userManager, HuiaPasskeyRegistrar registrar,
        IMultiTenantContextAccessor tenantAccessor, HuiaOptions options, PasskeyRegistrationRequest body)
    {
        if (!PasskeyEnabled(options, tenantAccessor))
        {
            return Results.NotFound();
        }

        var user = await ManageEndpoints.ResolveUserAsync(context, userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var outcome = await registrar.RegisterAsync(user, body.Credential.GetRawText(), body.Name);
        if (!outcome.Succeeded)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["credential"] = [outcome.Error ?? "The passkey could not be registered."],
            });
        }

        var passkey = (await userManager.GetPasskeysAsync(user))
            .First(p => WebEncoders.Base64UrlEncode(p.CredentialId) == outcome.CredentialId);
        return Results.Ok(ToDto(passkey));
    }

    private static async Task<IResult> ListAsync(HttpContext context, HuiaUserManager userManager)
    {
        var user = await ManageEndpoints.ResolveUserAsync(context, userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var passkeys = await userManager.GetPasskeysAsync(user);
        return Results.Ok(passkeys.Select(ToDto).ToArray());
    }

    private static async Task<IResult> RenameAsync(HttpContext context, HuiaUserManager userManager, string id, RenamePasskeyRequest body)
    {
        var user = await ManageEndpoints.ResolveUserAsync(context, userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        if (!TryDecodeId(id, out var credentialId))
        {
            return Results.NotFound();
        }

        return await userManager.RenamePasskeyAsync(user, credentialId, body.Name)
            ? Results.NoContent()
            : Results.NotFound();
    }

    private static async Task<IResult> RemoveAsync(
        HttpContext context, HuiaUserManager userManager, IMultiTenantContextAccessor tenantAccessor,
        IHuiaEventPublisher events, TimeProvider timeProvider, string id)
    {
        var user = await ManageEndpoints.ResolveUserAsync(context, userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        if (!TryDecodeId(id, out var credentialId))
        {
            return Results.NotFound();
        }

        if (await userManager.GetPasskeyAsync(user, credentialId) is null)
        {
            return Results.NotFound();
        }

        if (!await userManager.CanRemovePasskeyAsync(user))
        {
            return Results.Conflict(new { error = "only_sign_in_method" });
        }

        var result = await userManager.RemovePasskeyAsync(user, credentialId);
        if (!result.Succeeded)
        {
            return Problem(result);
        }

        await events.PublishAsync(new PasskeyRemovedEvent(
            tenantAccessor.RequireCurrentTenantId(), user.Id, Short(id), timeProvider.GetUtcNow()));
        return Results.NoContent();
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static bool PasskeyEnabled(HuiaOptions options, IMultiTenantContextAccessor tenantAccessor)
    {
        var tenantId = tenantAccessor.CurrentTenantId();
        return tenantId is not null
            && options.Tenants.TryGetValue(tenantId, out var tenant)
            && tenant.Authentication.Passkey is not null;
    }

    private static async Task<bool> ValidateAntiforgeryAsync(HttpContext context, IAntiforgery antiforgery)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }

    private static string ResolveRedirect(HttpContext context, string returnUrl, HuiaOptions options, string tenantId)
    {
        var pathBase = context.Request.PathBase.HasValue ? context.Request.PathBase.Value! : string.Empty;
        var tenantRoot = pathBase.Length == 0 ? "/" : pathBase + "/";
        var isTenantRootOnly = string.Equals(returnUrl, tenantRoot, StringComparison.Ordinal)
            || string.Equals(returnUrl, pathBase, StringComparison.Ordinal);

        if (!isTenantRootOnly)
        {
            return returnUrl;
        }

        var tenant = options.Tenants.TryGetValue(tenantId, out var value) ? value : null;
        return TenantClientHome.Resolve(tenant) ?? returnUrl;
    }

    private static bool TryDecodeId(string id, out byte[] credentialId)
    {
        try
        {
            credentialId = WebEncoders.Base64UrlDecode(id);
            return credentialId.Length > 0;
        }
        catch (FormatException)
        {
            credentialId = [];
            return false;
        }
    }

    private static string Short(string base64UrlId) => base64UrlId.Length <= 12 ? base64UrlId : base64UrlId[..12];

    private static PasskeyDto ToDto(UserPasskeyInfo p) => new(
        WebEncoders.Base64UrlEncode(p.CredentialId),
        p.Name,
        p.CreatedAt,
        p.IsBackedUp,
        p.IsUserVerified);

    private static IResult Problem(IdentityResult result) =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["identity"] = result.Errors.Select(e => e.Description).ToArray(),
        });

    private sealed record PasskeyDto(string Id, string? Name, DateTimeOffset CreatedAt, bool IsBackedUp, bool IsUserVerified);

    /// <summary>Body of the discoverable <c>POST identity/account/passkey/assertion</c>.</summary>
    /// <param name="Credential">The WebAuthn assertion produced by <c>navigator.credentials.get</c>.</param>
    /// <param name="ReturnUrl">Where to send the browser on success.</param>
    /// <param name="RememberMe">Whether to issue a persistent cookie.</param>
    public sealed record PasskeyAssertionRequest(JsonElement Credential, string? ReturnUrl, bool RememberMe);

    /// <summary>Body of <c>POST manage/passkeys</c>.</summary>
    /// <param name="Credential">The WebAuthn attestation produced by <c>navigator.credentials.create</c>.</param>
    /// <param name="Name">A friendly name for the credential.</param>
    public sealed record PasskeyRegistrationRequest(JsonElement Credential, string? Name);

    /// <summary>Body of <c>PATCH manage/passkeys/{id}</c>.</summary>
    /// <param name="Name">The new friendly name.</param>
    public sealed record RenamePasskeyRequest(string Name);
}
