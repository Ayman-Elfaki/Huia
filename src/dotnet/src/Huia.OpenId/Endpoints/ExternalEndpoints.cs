using System.Security.Claims;
using Finbuckle.MultiTenant.Abstractions;
using Huia.Events;
using Huia.Identity;
using Huia.Multitenancy;
using Huia.OpenId.Flows;
using Huia.OpenId.Identity;
using Huia.Options;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using OpenIddict.Client.AspNetCore;

namespace Huia.OpenId.Endpoints;

/// <summary>
/// External identity provider endpoints. Sign-in is driven by the OpenIddict client: a challenge starts
/// the upstream authorization-code flow and <c>signin-{provider}</c> is its redirection endpoint
/// (passthrough). The callback writes the standard <see cref="IdentityConstants.ExternalScheme"/> cookie
/// and hands off to <c>externallogincallback</c>, which uses <c>SignInManager.GetExternalLoginInfoAsync</c>
/// to link or sign in.
/// </summary>
internal static class ExternalEndpoints
{
    private const string ExternalLoginsPage = "identity/account/externallogins";
    private const string LoginPage = "identity/account/login";

    public static RouteGroupBuilder MapHuiaExternalEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("");

        group.MapPost("identity/account/external/{provider}", ChallengeAsync).WithName("huia.external.challenge");
        group.MapMethods("signin-{provider}", ["GET", "POST"], CallbackAsync).WithName("huia.external.callback");
        group.MapMethods("identity/account/externallogincallback", ["GET", "POST"], ExternalLoginCallbackAsync)
            .WithName("huia.external.dispatch");
        group.MapMethods("signout-callback-oidc", ["GET", "POST"], (Delegate)SignOutCallbackAsync).WithName("huia.external.signout-callback");

        return group;
    }

    /// <summary>
    /// OpenIddict client post-logout redirection endpoint. The upstream provider has ended its session;
    /// forward the browser to wherever <c>/connect/logout</c> decided the relying party should land (that
    /// target rode along in the client's signed logout state token).
    /// </summary>
    private static async Task<IResult> SignOutCallbackAsync(HttpContext context)
    {
        var result = await context.AuthenticateAsync(OpenIddictClientAspNetCoreDefaults.AuthenticationScheme);
        var target = result.Properties?.RedirectUri;
        return Results.Redirect(string.IsNullOrEmpty(target) ? $"{context.Request.PathBase}/" : target);
    }

    private static async Task<IResult> ChallengeAsync(
        HttpContext context,
        string provider,
        IAntiforgery antiforgery,
        IMultiTenantContextAccessor tenantAccessor,
        IReturnUrlProtector returnUrlProtector,
        HuiaOptions options)
    {
        await antiforgery.ValidateRequestAsync(context);

        var tenantId = tenantAccessor.RequireCurrentTenantId();
        if (!TryFindProvider(options, tenantId, provider, out var registered))
        {
            return Results.NotFound();
        }

        var returnUrl = returnUrlProtector.SanitizeReturnUrl(context.Request.Form["returnUrl"], context);

        var properties = new AuthenticationProperties(new Dictionary<string, string?>
        {
            [OpenIddictClientAspNetCoreConstants.Properties.RegistrationId] = $"{tenantId}:{registered.Name}",
        })
        {
            RedirectUri = $"{context.Request.PathBase}/{LoginPage}",
        };

        properties.Items["huia:tenant"] = tenantId;
        properties.Items["huia:provider"] = registered.Name;
        properties.Items["huia:return"] = returnUrl;

        return Results.Challenge(properties, [OpenIddictClientAspNetCoreDefaults.AuthenticationScheme]);
    }

    /// <summary>
    /// OpenIddict client redirection endpoint. Reads the upstream principal, then re-issues it as the
    /// ASP.NET Core external cookie and forwards to the dispatcher.
    /// </summary>
    private static async Task<IResult> CallbackAsync(
        HttpContext context,
        string provider,
        IMultiTenantContextAccessor tenantAccessor,
        HuiaOptions options)
    {
        var tenantId = tenantAccessor.RequireCurrentTenantId();
        if (!TryFindProvider(options, tenantId, provider, out var registered))
        {
            return Results.NotFound();
        }

        var result = await context.AuthenticateAsync(OpenIddictClientAspNetCoreDefaults.AuthenticationScheme);
        if (!result.Succeeded || result.Principal is null)
        {
            return Results.Redirect($"{context.Request.PathBase}/{LoginPage}");
        }

        var subject = FirstClaim(result.Principal, "sub", ClaimTypes.NameIdentifier, "id");
        if (subject is null)
        {
            return Results.Redirect($"{context.Request.PathBase}/{LoginPage}");
        }

        var registrationId = $"{tenantId}:{registered.Name}";
        var email = FirstClaim(result.Principal, "email", ClaimTypes.Email);
        var emailVerified = FirstClaim(result.Principal, "email_verified");
        var givenName = FirstClaim(result.Principal, "given_name", ClaimTypes.GivenName);
        var familyName = FirstClaim(result.Principal, "family_name", ClaimTypes.Surname);
        var displayName = FirstClaim(result.Principal, "name") ?? $"{givenName} {familyName}".Trim();
        var storedReturn = result.Properties?.Items is { } items && items.TryGetValue("huia:return", out var r) ? r : null;
        var idToken = result.Properties?.GetTokenValue("id_token");

        var identity = new ClaimsIdentity(IdentityConstants.ExternalScheme);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, subject));
        AddIfPresent(identity, ClaimTypes.Email, email);
        AddIfPresent(identity, "email_verified", emailVerified);
        AddIfPresent(identity, ClaimTypes.GivenName, givenName);
        AddIfPresent(identity, ClaimTypes.Surname, familyName);
        AddIfPresent(identity, "name", string.IsNullOrWhiteSpace(displayName) ? null : displayName);

        var properties = new AuthenticationProperties();
        properties.Items["LoginProvider"] = registrationId;
        properties.Items["huia:tenant"] = tenantId;
        properties.Items["huia:provider"] = registered.Name;
        properties.Items["huia:return"] = storedReturn;
        if (!string.IsNullOrEmpty(idToken) && idToken.Length <= 3072)
        {
            properties.Items["huia:ext_id_token"] = idToken;
        }

        await context.SignInAsync(IdentityConstants.ExternalScheme, new ClaimsPrincipal(identity), properties);
        return Results.Redirect($"{context.Request.PathBase}/identity/account/externallogincallback");
    }

    /// <summary>
    /// Dispatches an external sign-in: link it to the currently signed-in user (Scenario 1), sign in an
    /// already-linked account, auto-link a confirmed-email match (Scenario 2, opt-in), or collect a name
    /// for a brand-new account.
    /// </summary>
    private static async Task<IResult> ExternalLoginCallbackAsync(
        HttpContext context,
        IHuiaFlowIdentityFactory flowIdentity,
        IMultiTenantContextAccessor tenantAccessor,
        IReturnUrlProtector returnUrlProtector,
        IHuiaEventPublisher events,
        HuiaOptions options,
        TimeProvider timeProvider)
    {
        var external = flowIdentity.Create(HuiaAuthFlow.ExternalLogin);
        var userManager = external.UserManager;
        var signInManager = external.SignInManager;

        var tenantId = tenantAccessor.RequireCurrentTenantId();
        var pathBase = context.Request.PathBase.Value ?? string.Empty;

        var info = await signInManager.GetExternalLoginInfoAsync();
        if (info is null)
        {
            return Results.Redirect($"{pathBase}/{LoginPage}");
        }

        var items = info.AuthenticationProperties?.Items ?? new Dictionary<string, string?>();
        var returnUrl = returnUrlProtector.SanitizeReturnUrl(Item(items, "huia:return"), context);
        var idToken = Item(items, "huia:ext_id_token");
        var providerName = info.LoginProvider.Contains(':', StringComparison.Ordinal)
            ? info.LoginProvider[(info.LoginProvider.LastIndexOf(':') + 1)..]
            : info.LoginProvider;

        var email = info.Principal.FindFirstValue(ClaimTypes.Email);
        var emailVerified = info.Principal.FindFirstValue("email_verified");
        var displayName = info.Principal.FindFirstValue("name") ?? info.ProviderDisplayName;
        var givenName = info.Principal.FindFirstValue(ClaimTypes.GivenName);
        var familyName = info.Principal.FindFirstValue(ClaimTypes.Surname);

        // --- Scenario 1: a signed-in user is linking a provider from account settings. ---
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var current = await userManager.GetUserAsync(context.User);
            await context.SignOutAsync(IdentityConstants.ExternalScheme);
            if (current is null)
            {
                return Results.Redirect($"{pathBase}/{ExternalLoginsPage}?linked=error");
            }

            var owner = await userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
            if (owner is not null)
            {
                return Results.Redirect($"{pathBase}/{ExternalLoginsPage}?linked=" + (owner.Id == current.Id ? "ok" : "dupe"));
            }

            var linked = await userManager.AddExternalLoginAsync(current, info.LoginProvider, info.ProviderKey, providerName);
            return Results.Redirect($"{pathBase}/{ExternalLoginsPage}?linked=" + (linked.Succeeded ? "ok" : "error"));
        }

        await context.SignOutAsync(IdentityConstants.ExternalScheme);

        // --- Scenario 2: logged out. ---
        var user = await userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);

        if (user is null && !string.IsNullOrWhiteSpace(email))
        {
            var providerVouches = !string.Equals(emailVerified, "false", StringComparison.OrdinalIgnoreCase);
            var accountLinkingEnabled = options.Tenants.TryGetValue(tenantId, out var tenant)
                && tenant.GetHuiaOpenId()?.External?.AccountLinkingEnabled == true;

            var (outcome, linked) = await userManager.TryLinkExternalByEmailAsync(
                email, providerVouches, accountLinkingEnabled, info.LoginProvider, info.ProviderKey, providerName);

            switch (outcome)
            {
                case ExternalEmailLinkOutcome.Linked:
                    user = linked;
                    break;
                case ExternalEmailLinkOutcome.Blocked:
                    // An account already owns this email; linking is disabled or ineligible. Never create a duplicate.
                    return Results.Redirect($"{pathBase}/{LoginPage}?linkError=1");
            }
        }

        if (user is not null)
        {
            await SignInExternalAsync(signInManager, events, user, providerName, info.LoginProvider, idToken, tenantId, timeProvider);
            return Results.LocalRedirect(returnUrl);
        }

        // First time here — collect a name before creating the account.
        var flow = returnUrlProtector.Tokenize(new AuthFlowState
        {
            ReturnUrl = returnUrl,
            ExternalProvider = info.LoginProvider,
            ExternalProviderKey = info.ProviderKey,
            ExternalDisplayName = displayName,
            Email = email,
            FirstName = givenName,
            LastName = familyName,
            ExternalIdToken = idToken,
        });

        return Results.Redirect($"{pathBase}/identity/account/completeprofile?flow={Uri.EscapeDataString(flow)}");
    }

    private static async Task SignInExternalAsync(
        SignInManager<HuiaUser> signInManager,
        IHuiaEventPublisher events,
        HuiaUser user,
        string providerName,
        string registrationId,
        string? idToken,
        string tenantId,
        TimeProvider timeProvider)
    {
        var claims = new List<Claim>
        {
            new(HuiaConstants.ClaimTypes.AuthenticationMethod, providerName),
            new(HuiaOpenIdConstants.ClaimTypes.ExternalIdp, registrationId),
        };
        if (!string.IsNullOrEmpty(idToken))
        {
            claims.Add(new Claim(HuiaOpenIdConstants.ClaimTypes.ExternalIdToken, idToken));
        }

        await signInManager.SignInWithClaimsAsync(user, isPersistent: false, claims);
        await events.PublishAsync(new UserLoggedInEvent(tenantId, user.Id, providerName, null, timeProvider.GetUtcNow()));
    }

    private static bool TryFindProvider(HuiaOptions options, string tenantId, string providerName, out ExternalProviderRegistration provider)
    {
        provider = null!;
        if (!options.Tenants.TryGetValue(tenantId, out var tenant))
        {
            return false;
        }

        var external = tenant.GetHuiaOpenId()?.External;
        var match = external?.Providers.FirstOrDefault(p => string.Equals(p.Name, providerName, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return false;
        }

        provider = match;
        return true;
    }

    private static string? Item(IDictionary<string, string?> items, string key) =>
        items.TryGetValue(key, out var value) ? value : null;

    private static void AddIfPresent(ClaimsIdentity identity, string type, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            identity.AddClaim(new Claim(type, value));
        }
    }

    private static string? FirstClaim(ClaimsPrincipal principal, params string[] types)
    {
        foreach (var type in types)
        {
            var value = principal.FindFirstValue(type);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
