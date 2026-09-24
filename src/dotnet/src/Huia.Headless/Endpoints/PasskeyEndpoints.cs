using System.Text.Json;
using Huia.Entities;
using Huia.Events;
using Huia.Headless.Identity;
using Huia.Identity;
using Huia.Multitenancy;
using Huia.Options;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;

namespace Huia.Headless.Endpoints;

/// <summary>
/// Passkey (WebAuthn / FIDO2) endpoints for the single-tenant Headless flavor: the anonymous
/// discoverable sign-in routes under <c>identity/passkey/*</c>, and the bearer-protected
/// credential-management routes under <c>identity/manage/passkeys</c>. Every route 404s when the host's
/// one tenant has not called <c>UsePasskeyLogin()</c>. Shares <see cref="HuiaPasskeyRegistrar{TUser}"/>
/// with the OpenId flavor — only the HTTP plumbing here is Headless-specific.
/// </summary>
internal static class PasskeyEndpoints
{
    /// <summary>Maps the anonymous passkey ceremony and sign-in endpoints.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    public static void MapHuiaHeadlessPasskeyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("identity/passkey");
        group.MapPost("assertion-options", AssertionOptionsAsync).WithName("huia.headless.passkey.assertion-options");
        group.MapPost("assertion", AssertionAsync).WithName("huia.headless.passkey.assertion");

        var manage = endpoints.MapGroup("identity/manage/passkeys").RequireAuthorization();
        manage.MapPost("creation-options", CreationOptionsAsync).WithName("huia.headless.passkey.creation-options");
        manage.MapGet("", ListAsync).WithName("huia.headless.passkey.list");
        manage.MapPost("", RegisterAsync).WithName("huia.headless.passkey.register");
        manage.MapPatch("{id}", RenameAsync).WithName("huia.headless.passkey.rename");
        manage.MapDelete("{id}", RemoveAsync).WithName("huia.headless.passkey.remove");
    }

    // ---- anonymous: discoverable primary sign-in --------------------------------------------------

    private static async Task<IResult> AssertionOptionsAsync(
        HttpContext context, HuiaSignInManager<HuiaUser> signInManager, IAntiforgery antiforgery, TenantOptions tenant)
    {
        if (!tenant.Authentication.IsPasskeyLoginEnabled)
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
        HttpContext context, HuiaSignInManager<HuiaUser> signInManager, IAntiforgery antiforgery,
        IHuiaTenantContext tenantContext, IHuiaEventPublisher events, TimeProvider timeProvider,
        TenantOptions tenant, PasskeyAssertionRequest body)
    {
        if (!tenant.Authentication.IsPasskeyLoginEnabled)
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
            // Bearer scheme, not the cookie default — the BearerTokenHandler writes the
            // {accessToken, refreshToken, expiresIn} response body itself, the same way
            // MapIdentityApi's own /login does; there is no interactive session to redirect from.
            signInManager.AuthenticationScheme = IdentityConstants.BearerScheme;
            (result, user) = await signInManager.PasskeyPrimarySignInAsync(body.Credential.GetRawText(), isPersistent: false);
        }
        catch (InvalidOperationException)
        {
            return Results.BadRequest(new { error = "no_ceremony_in_progress" });
        }

        if (!result.Succeeded || user is null)
        {
            return Results.BadRequest(new { error = result.IsLockedOut ? "locked_out" : "assertion_failed" });
        }

        await events.PublishAsync(new UserLoggedInEvent(
            tenantContext.CurrentTenantId, user.Id, HuiaConstants.AuthenticationMethods.Passkey, null, timeProvider.GetUtcNow()));

        return Results.Empty;
    }

    // ---- bearer-protected: credential management ------------------------------------------------

    private static async Task<IResult> CreationOptionsAsync(
        HttpContext context, HuiaSignInManager<HuiaUser> signInManager, HuiaUserManager userManager, TenantOptions tenant)
    {
        if (!tenant.Authentication.IsPasskeyLoginEnabled)
        {
            return Results.NotFound();
        }

        var user = await ResolveUserAsync(context, userManager);
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
        HttpContext context, HuiaUserManager userManager, HuiaPasskeyRegistrar<HuiaUser> registrar,
        TenantOptions tenant, PasskeyRegistrationRequest body)
    {
        if (!tenant.Authentication.IsPasskeyLoginEnabled)
        {
            return Results.NotFound();
        }

        var user = await ResolveUserAsync(context, userManager);
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
        var user = await ResolveUserAsync(context, userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var passkeys = await userManager.GetPasskeysAsync(user);
        return Results.Ok(passkeys.Select(ToDto).ToArray());
    }

    private static async Task<IResult> RenameAsync(HttpContext context, HuiaUserManager userManager, string id, RenamePasskeyRequest body)
    {
        var user = await ResolveUserAsync(context, userManager);
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
        HttpContext context, HuiaUserManager userManager, IHuiaTenantContext tenantContext,
        IHuiaEventPublisher events, TimeProvider timeProvider, string id)
    {
        var user = await ResolveUserAsync(context, userManager);
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
            tenantContext.CurrentTenantId, user.Id, Short(id), timeProvider.GetUtcNow()));
        return Results.NoContent();
    }

    // ---- helpers -------------------------------------------------------------------------------

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

    private static async Task<HuiaUser?> ResolveUserAsync(HttpContext context, HuiaUserManager userManager) =>
        await userManager.GetUserAsync(context.User);

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

    /// <summary>Body of the discoverable <c>POST identity/passkey/assertion</c>.</summary>
    /// <param name="Credential">The WebAuthn assertion produced by <c>navigator.credentials.get</c>.</param>
    public sealed record PasskeyAssertionRequest(JsonElement Credential);

    /// <summary>Body of <c>POST identity/manage/passkeys</c>.</summary>
    /// <param name="Credential">The WebAuthn attestation produced by <c>navigator.credentials.create</c>.</param>
    /// <param name="Name">A friendly name for the credential.</param>
    public sealed record PasskeyRegistrationRequest(JsonElement Credential, string? Name);

    /// <summary>Body of <c>PATCH identity/manage/passkeys/{id}</c>.</summary>
    /// <param name="Name">The new friendly name.</param>
    public sealed record RenamePasskeyRequest(string Name);
}
