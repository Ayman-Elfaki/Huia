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
/// Passkey (WebAuthn / FIDO2) endpoints. The anonymous ceremony + sign-in routes live under
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
        group.MapPost("2fa-options", TwoFactorOptionsAsync).WithName("huia.passkey.2fa-options");
        group.MapPost("2fa", TwoFactorAssertionAsync).WithName("huia.passkey.2fa");

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
        group.MapGet("two-factor", GetTwoFactorAsync);
        group.MapPut("two-factor", SetTwoFactorAsync);
        group.MapPost("recovery-codes", RegenerateRecoveryCodesAsync);
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

    // ---- anonymous: passkey as a second factor --------------------------------------------------

    private static async Task<IResult> TwoFactorOptionsAsync(
        HttpContext context, HuiaSignInManager signInManager, HuiaUserManager userManager, IAntiforgery antiforgery,
        IMultiTenantContextAccessor tenantAccessor, IReturnUrlProtector returnUrlProtector,
        HuiaOptions options, PasskeyFlowRequest body)
    {
        if (!PasskeyEnabled(options, tenantAccessor, out var policy) || policy is not { AllowSecondFactor: true })
        {
            return Results.NotFound();
        }

        if (!await ValidateAntiforgeryAsync(context, antiforgery))
        {
            return Results.BadRequest(new { error = "antiforgery" });
        }

        var state = returnUrlProtector.Read(body.Flow);
        if (state?.UserId is not { } userId || await userManager.FindByIdAsync(userId) is not { } user)
        {
            return Results.BadRequest(new { error = "no_pending_two_factor" });
        }

        var json = await signInManager.MakePasskeyRequestOptionsAsync(user);
        return Results.Content(json, "application/json");
    }

    private static async Task<IResult> TwoFactorAssertionAsync(
        HttpContext context, HuiaSignInManager signInManager, IAntiforgery antiforgery,
        IMultiTenantContextAccessor tenantAccessor, IReturnUrlProtector returnUrlProtector,
        IHuiaEventPublisher events, TimeProvider timeProvider, HuiaOptions options, PasskeyTwoFactorRequest body)
    {
        if (!PasskeyEnabled(options, tenantAccessor, out var policy) || policy is not { AllowSecondFactor: true })
        {
            return Results.NotFound();
        }

        if (!await ValidateAntiforgeryAsync(context, antiforgery))
        {
            return Results.BadRequest(new { error = "antiforgery" });
        }

        var state = returnUrlProtector.Read(body.Flow);
        if (state?.UserId is not { } userId)
        {
            return Results.BadRequest(new { error = "no_pending_two_factor" });
        }

        SignInResult result;
        try
        {
            result = await signInManager.PasskeyStepUpSignInAsync(
                userId, body.Credential.GetRawText(), isPersistent: false, rememberClient: body.RememberMachine);
        }
        catch (InvalidOperationException)
        {
            return Results.BadRequest(new { error = "no_ceremony_in_progress" });
        }

        if (!result.Succeeded)
        {
            return Results.BadRequest(new { error = result.IsLockedOut ? "locked_out" : "assertion_failed" });
        }

        var tenantId = tenantAccessor.RequireCurrentTenantId();
        await events.PublishAsync(new UserLoggedInEvent(
            tenantId, userId, HuiaConstants.AuthenticationMethods.MultiFactor, null, timeProvider.GetUtcNow()));

        var returnUrl = returnUrlProtector.SanitizeReturnUrl(state.ReturnUrl, context);
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
        HttpContext context, HuiaSignInManager signInManager, HuiaUserManager userManager,
        IMultiTenantContextAccessor tenantAccessor, IHuiaEventPublisher events, TimeProvider timeProvider,
        HuiaOptions options, PasskeyRegistrationRequest body)
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

        PasskeyAttestationResult attestation;
        try
        {
            attestation = await signInManager.PerformPasskeyAttestationAsync(body.Credential.GetRawText());
        }
        catch (InvalidOperationException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["credential"] = ["No passkey registration is in progress."],
            });
        }

        if (!attestation.Succeeded || attestation.Passkey is not { } passkeyInfo)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["credential"] = [attestation.Failure?.Message ?? "The passkey could not be registered."],
            });
        }

        passkeyInfo.Name = string.IsNullOrWhiteSpace(body.Name) ? null : body.Name.Trim();
        var result = await userManager.AddOrUpdatePasskeyAsync(user, passkeyInfo);
        if (!result.Succeeded)
        {
            return Problem(result);
        }

        var id = WebEncoders.Base64UrlEncode(passkeyInfo.CredentialId);
        await events.PublishAsync(new PasskeyRegisteredEvent(
            tenantAccessor.RequireCurrentTenantId(), user.Id, Short(id), timeProvider.GetUtcNow()));

        return Results.Ok(ToDto(passkeyInfo));
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

        if (user.TwoFactorEnabled && await userManager.CountPasskeysAsync(user) <= 1)
        {
            return Results.Conflict(new { error = "last_passkey_two_factor_enabled" });
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

    private static async Task<IResult> GetTwoFactorAsync(HttpContext context, HuiaUserManager userManager)
    {
        var user = await ManageEndpoints.ResolveUserAsync(context, userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(new PasskeyTwoFactorDto(
            user.TwoFactorEnabled,
            await userManager.HasPasskeyAsync(user),
            await userManager.CountRecoveryCodesAsync(user)));
    }

    private static async Task<IResult> SetTwoFactorAsync(
        HttpContext context, HuiaUserManager userManager, IMultiTenantContextAccessor tenantAccessor,
        HuiaOptions options, SetPasskeyTwoFactorRequest body)
    {
        if (!PasskeyEnabled(options, tenantAccessor, out var policy))
        {
            return Results.NotFound();
        }

        var user = await ManageEndpoints.ResolveUserAsync(context, userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        if (body.Enabled)
        {
            if (policy is not { AllowSecondFactor: true })
            {
                return Results.NotFound();
            }

            if (!await userManager.HasPasskeyAsync(user))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["passkey"] = ["Register a passkey before requiring it as a second factor."],
                });
            }

            var enable = await userManager.SetTwoFactorEnabledAsync(user, true);
            if (!enable.Succeeded)
            {
                return Problem(enable);
            }

            var codes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
            return Results.Ok(new { enabled = true, recoveryCodes = codes?.ToArray() ?? [] });
        }

        var disable = await userManager.SetTwoFactorEnabledAsync(user, false);
        return disable.Succeeded ? Results.Ok(new { enabled = false, recoveryCodes = Array.Empty<string>() }) : Problem(disable);
    }

    private static async Task<IResult> RegenerateRecoveryCodesAsync(HttpContext context, HuiaUserManager userManager)
    {
        var user = await ManageEndpoints.ResolveUserAsync(context, userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        if (!user.TwoFactorEnabled)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["twoFactor"] = ["Two-factor authentication is not enabled."],
            });
        }

        var codes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        return Results.Ok(new { recoveryCodes = codes?.ToArray() ?? [] });
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static bool PasskeyEnabled(HuiaOptions options, IMultiTenantContextAccessor tenantAccessor) =>
        PasskeyEnabled(options, tenantAccessor, out _);

    private static bool PasskeyEnabled(HuiaOptions options, IMultiTenantContextAccessor tenantAccessor, out PasskeyOptions? policy)
    {
        policy = null;
        var tenantId = tenantAccessor.CurrentTenantId();
        if (tenantId is null || !options.Tenants.TryGetValue(tenantId, out var tenant) || tenant.Authentication.Passkey is not { } configured)
        {
            return false;
        }

        policy = configured;
        return true;
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

    private sealed record PasskeyTwoFactorDto(bool Enabled, bool HasPasskey, int RecoveryCodesLeft);

    /// <summary>Body of the discoverable <c>POST identity/account/passkey/assertion</c>.</summary>
    /// <param name="Credential">The WebAuthn assertion produced by <c>navigator.credentials.get</c>.</param>
    /// <param name="ReturnUrl">Where to send the browser on success.</param>
    /// <param name="RememberMe">Whether to issue a persistent cookie.</param>
    public sealed record PasskeyAssertionRequest(JsonElement Credential, string? ReturnUrl, bool RememberMe);

    /// <summary>Body of <c>POST identity/account/passkey/2fa-options</c>.</summary>
    /// <param name="Flow">The protected flow token carrying the pending user id and return URL.</param>
    public sealed record PasskeyFlowRequest(string? Flow);

    /// <summary>Body of <c>POST identity/account/passkey/2fa</c>.</summary>
    /// <param name="Credential">The WebAuthn assertion.</param>
    /// <param name="Flow">The protected flow token from the password step.</param>
    /// <param name="RememberMachine">Whether to remember this browser and skip the second factor next time.</param>
    public sealed record PasskeyTwoFactorRequest(JsonElement Credential, string? Flow, bool RememberMachine);

    /// <summary>Body of <c>POST manage/passkeys</c>.</summary>
    /// <param name="Credential">The WebAuthn attestation produced by <c>navigator.credentials.create</c>.</param>
    /// <param name="Name">A friendly name for the credential.</param>
    public sealed record PasskeyRegistrationRequest(JsonElement Credential, string? Name);

    /// <summary>Body of <c>PATCH manage/passkeys/{id}</c>.</summary>
    /// <param name="Name">The new friendly name.</param>
    public sealed record RenamePasskeyRequest(string Name);

    /// <summary>Body of <c>PUT manage/passkeys/two-factor</c>.</summary>
    /// <param name="Enabled">Whether a passkey is required as a second factor.</param>
    public sealed record SetPasskeyTwoFactorRequest(bool Enabled);
}
