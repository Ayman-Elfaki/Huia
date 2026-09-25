using System.Security.Claims;
using Huia.Entities;
using Huia.Events;
using Huia.Headless.Identity;
using Huia.Headless.Services;
using Huia.Identity;
using Huia.Multitenancy;
using Huia.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;

namespace Huia.Headless.Endpoints;

/// <summary>
/// External identity provider endpoints for the single-tenant Headless flavor. Unlike <c>Huia.OpenId</c>
/// (built on the OpenIddict client), this uses the classic ASP.NET Core remote-authentication handlers
/// registered per provider in <c>AddHuiaHeadless()</c> — the challenge/callback mechanics are the
/// framework's; the endpoints here only cover the parts specific to Huia (link-or-create, and handing the
/// app a bearer token instead of a cookie).
///
/// Because the browser starts on the app's own origin, is sent to the provider, and needs to end back on
/// the app's origin — never Huia.Headless's — the callback dispatches to a one-time <c>code</c> in the
/// query string (never a token) via a redirect to the caller-supplied, allow-listed <c>returnUrl</c>. The
/// app's own server exchanges that code for the actual bearer token with a plain <c>POST</c>, so the
/// token itself never appears in a URL or browser history. This mirrors the two-step
/// <c>start</c>/<c>verify</c> shape of <see cref="PhoneEndpoints"/>, and only covers a logged-out
/// sign-in/sign-up — linking a provider to an already-authenticated account (Huia.OpenId's "Scenario 1")
/// is not implemented here.
/// </summary>
internal static class ExternalEndpoints
{
    public static void MapHuiaHeadlessExternalEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("identity/account/external");
        group.MapGet("{provider}", ChallengeAsync).WithName(HuiaConstants.Endpoints.Headless.External.Challenge);
        group.MapGet("callback", DispatchAsync).WithName(HuiaConstants.Endpoints.Headless.External.Callback);
        group.MapPost("exchange", ExchangeAsync).WithName(HuiaConstants.Endpoints.Headless.External.Exchange);
        group.MapPost("complete-profile", CompleteProfileAsync).WithName(HuiaConstants.Endpoints.Headless.External.CompleteProfile);
    }

    private static IResult ChallengeAsync(HttpContext context, string provider, string returnUrl, TenantOptions tenant)
    {
        if (!TryFindProvider(tenant, provider, out var registered, out var external))
        {
            return Results.NotFound();
        }

        if (!IsAllowedReturnUrl(returnUrl, external.AllowedReturnUrlPrefixes))
        {
            return Results.BadRequest(new { error = "invalid_return_url" });
        }

        var properties = new AuthenticationProperties
        {
            RedirectUri = $"{context.Request.PathBase}/identity/account/external/callback",
        };
        // The well-known key ASP.NET Core Identity's own SignInManager.GetExternalLoginInfoAsync() reads
        // back on the other side (mirroring Huia.OpenId's ChallengeAsync) — result.Ticket.AuthenticationScheme
        // is always just "Identity.External" itself, never the provider name, so this Items entry is the
        // only place the provider identity survives the round trip through the provider and back.
        properties.Items["LoginProvider"] = registered.Name;
        properties.Items["huia:return"] = returnUrl;

        return Results.Challenge(properties, [registered.Name]);
    }

    /// <summary>
    /// The provider's remote-authentication handler already signed the resolved principal into
    /// <see cref="IdentityConstants.ExternalScheme"/> and redirected here (the challenge's
    /// <c>RedirectUri</c>). Link-or-create, then redirect to the caller's <c>returnUrl</c> with a
    /// one-time code — never a token.
    /// </summary>
    private static async Task<IResult> DispatchAsync(
        HttpContext context, HuiaUserManager userManager, HuiaSignInManager<HuiaUser> signInManager,
        IExternalLoginFlowStore flows, TenantOptions tenant)
    {
        var info = await signInManager.GetExternalLoginInfoAsync();
        if (info is null)
        {
            return Results.BadRequest(new { error = "external_sign_in_failed" });
        }

        await context.SignOutAsync(IdentityConstants.ExternalScheme);

        var items = info.AuthenticationProperties?.Items ?? new Dictionary<string, string?>();
        var returnUrl = items.TryGetValue("huia:return", out var r) ? r : null;
        if (tenant.Authentication.External is not { } external
            || returnUrl is null || !IsAllowedReturnUrl(returnUrl, external.AllowedReturnUrlPrefixes))
        {
            return Results.BadRequest(new { error = "invalid_return_url" });
        }

        var schemeName = info.LoginProvider;
        var providerKey = info.ProviderKey;

        var email = info.Principal.FindFirstValue(ClaimTypes.Email);
        var displayName = info.Principal.FindFirstValue(ClaimTypes.Name) ?? info.ProviderDisplayName ?? schemeName;
        var givenName = info.Principal.FindFirstValue(ClaimTypes.GivenName);
        var familyName = info.Principal.FindFirstValue(ClaimTypes.Surname);
        if (givenName is null && familyName is null && !string.IsNullOrWhiteSpace(displayName))
        {
            var parts = displayName.Split(' ', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            givenName = parts.ElementAtOrDefault(0);
            familyName = parts.ElementAtOrDefault(1);
        }

        var user = await userManager.FindByLoginAsync(schemeName, providerKey);

        if (user is null && !string.IsNullOrWhiteSpace(email))
        {
            var (outcome, linked) = await userManager.TryLinkExternalByEmailAsync(
                email, providerVouches: true, external.AccountLinkingEnabled, schemeName, providerKey, displayName);

            switch (outcome)
            {
                case ExternalEmailLinkOutcome.Linked:
                    user = linked;
                    break;
                case ExternalEmailLinkOutcome.Blocked:
                    return Results.Json(new { error = "email_already_registered" }, statusCode: StatusCodes.Status409Conflict);
            }
        }

        string code;
        if (user is not null)
        {
            code = flows.CreateForUser(user.Id, schemeName);
        }
        else
        {
            // First time here — the app collects a name before the account is created, mirroring
            // Huia.OpenId's CompleteProfile step; the provider's own claims are only a pre-fill default.
            code = flows.CreateForSignup(new ExternalSignup(schemeName, providerKey, displayName, email, givenName, familyName));
        }

        return Results.Redirect($"{returnUrl}{(returnUrl.Contains('?', StringComparison.Ordinal) ? '&' : '?')}code={Uri.EscapeDataString(code)}");
    }

    private static async Task<IResult> ExchangeAsync(
        HuiaUserManager userManager, HuiaSignInManager<HuiaUser> signInManager, IExternalLoginFlowStore flows,
        IHuiaEventPublisher events, TimeProvider timeProvider, IHuiaTenantContext tenantContext, ExchangeExternalCodeRequest body)
    {
        var flow = flows.Get(body.Code);
        if (flow is null)
        {
            return Problem();
        }

        if (flow.PendingSignup is { } signup)
        {
            return Results.Ok(new ExchangeExternalCodeResponse(
                Code: flow.Code, RequiresProfile: true, Email: signup.Email, FirstName: signup.FirstName, LastName: signup.LastName));
        }

        var user = flow.UserId is { } userId ? await userManager.FindByIdAsync(userId) : null;
        if (user is null || flow.LoginProvider is not { } loginProvider)
        {
            return Problem();
        }

        flows.Remove(flow.Code);
        await SignInAsync(signInManager, events, user, loginProvider, tenantContext.CurrentTenantId, timeProvider);
        return Results.Empty;
    }

    private static async Task<IResult> CompleteProfileAsync(
        HuiaUserManager userManager, HuiaSignInManager<HuiaUser> signInManager, IExternalLoginFlowStore flows,
        IHuiaEventPublisher events, TimeProvider timeProvider, IHuiaTenantContext tenantContext,
        CompleteExternalProfileRequest body)
    {
        var flow = flows.Get(body.Code);
        if (flow?.PendingSignup is not { } signup)
        {
            return Problem();
        }

        var tenantId = tenantContext.CurrentTenantId;

        // Defensive: an account may have claimed this email since the callback decided to provision.
        if (!string.IsNullOrWhiteSpace(signup.Email) && await userManager.FindByEmailAsync(signup.Email) is not null)
        {
            return Results.Json(new { error = "email_already_registered" }, statusCode: StatusCodes.Status409Conflict);
        }

        var (create, user) = await userManager.CreateExternalUserAsync(
            tenantId, signup.Email, body.FirstName, body.LastName, signup.LoginProvider, signup.ProviderKey, signup.ProviderDisplayName);
        if (!create.Succeeded)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["identity"] = create.Errors.Select(e => e.Description).ToArray(),
            });
        }

        flows.Remove(flow.Code);
        await events.PublishAsync(new UserRegisteredEvent(
            tenantId, user.Id, user.UserName!, signup.Email, signup.LoginProvider, timeProvider.GetUtcNow()));
        await SignInAsync(signInManager, events, user, signup.LoginProvider, tenantId, timeProvider);
        return Results.Empty;
    }

    private static async Task SignInAsync(
        HuiaSignInManager<HuiaUser> signInManager, IHuiaEventPublisher events, HuiaUser user, string loginProvider,
        string tenantId, TimeProvider timeProvider)
    {
        signInManager.AuthenticationScheme = IdentityConstants.BearerScheme;
        await signInManager.SignInWithClaimsAsync(user, isPersistent: false,
            [new Claim(HuiaConstants.ClaimTypes.AuthenticationMethod, loginProvider),
             new Claim(HuiaConstants.ClaimTypes.ExternalIdp, loginProvider)]);
        await events.PublishAsync(new UserLoggedInEvent(tenantId, user.Id, loginProvider, null, timeProvider.GetUtcNow()));
    }

    private static bool TryFindProvider(
        TenantOptions tenant, string providerName, out ExternalProviderRegistration provider, out ExternalLoginOptions external)
    {
        provider = null!;
        external = null!;
        if (tenant.Authentication.External is not { } ext)
        {
            return false;
        }

        var match = ext.Providers.FirstOrDefault(p => string.Equals(p.Name, providerName, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return false;
        }

        provider = match;
        external = ext;
        return true;
    }

    private static bool IsAllowedReturnUrl(string returnUrl, IReadOnlyList<string> allowedPrefixes) =>
        Uri.TryCreate(returnUrl, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
        && allowedPrefixes.Any(prefix => returnUrl.StartsWith(prefix, StringComparison.Ordinal));

    private static IResult Problem() => Results.BadRequest(new { error = "invalid" });

    /// <summary>Body of <c>POST identity/account/external/exchange</c>.</summary>
    /// <param name="Code">The one-time code from the callback redirect.</param>
    public sealed record ExchangeExternalCodeRequest(string Code);

    /// <summary>Response of <c>POST identity/account/external/exchange</c> for a new sign-up.</summary>
    /// <param name="Code">Pass this to <c>complete-profile</c>.</param>
    /// <param name="RequiresProfile">Always <see langword="true"/> — otherwise the bearer token response body is returned instead.</param>
    /// <param name="Email">The provider-supplied email, if any (read-only context, not resubmitted).</param>
    /// <param name="FirstName">A best-effort default for the profile form.</param>
    /// <param name="LastName">A best-effort default for the profile form.</param>
    public sealed record ExchangeExternalCodeResponse(string Code, bool RequiresProfile, string? Email, string? FirstName, string? LastName);

    /// <summary>Body of <c>POST identity/account/external/complete-profile</c>.</summary>
    /// <param name="Code">The code from an <c>exchange</c> response with <c>requiresProfile: true</c>.</param>
    /// <param name="FirstName">The account's first name.</param>
    /// <param name="LastName">The account's last name.</param>
    public sealed record CompleteExternalProfileRequest(string Code, string FirstName, string LastName);
}
