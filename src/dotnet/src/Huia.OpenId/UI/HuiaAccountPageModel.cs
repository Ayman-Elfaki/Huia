using Finbuckle.MultiTenant.Abstractions;
using Huia.Identity;
using Huia.Multitenancy;
using Huia.OpenId;
using Huia.OpenId.Flows;
using Huia.OpenId.Options;
using Huia.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.OpenId.UI;

/// <summary>Base for the account-UI page models. Exposes the resolved tenant and its options.</summary>
public abstract class HuiaAccountPageModel : PageModel
{
    /// <summary>The tenant the request resolved to. <see langword="null"/> only outside a tenant scope.</summary>
    protected string? TenantId =>
        HttpContext.RequestServices.GetService<IMultiTenantContextAccessor>()?.CurrentTenantId();

    /// <summary>The resolved tenant's options, or <see langword="null"/>.</summary>
    protected TenantOptions? Tenant
    {
        get
        {
            var options = (HuiaOptions?)HttpContext.RequestServices.GetService(typeof(HuiaOptions));
            var tenantId = TenantId;
            return options is not null && tenantId is not null && options.Tenants.TryGetValue(tenantId, out var tenant)
                ? tenant
                : null;
        }
    }

    /// <summary>Whether the interactive email/password form should be shown.</summary>
    public bool IsEmailAndPasswordLoginEnabled => Tenant?.Authentication.EmailAndPassword.Enabled ?? true;

    /// <summary>Whether anonymous visitors may register — controls whether the sign-up link is shown.</summary>
    public bool IsSelfServiceRegistrationEnabled =>
        Tenant?.Authentication.EmailAndPassword.AllowSelfServiceRegistration ?? false;

    /// <summary>Whether the passwordless phone (SMS one-time code) tab should be shown.</summary>
    public bool IsPhoneLoginEnabled => Tenant?.Authentication.IsPhoneLoginEnabled ?? false;

    /// <summary>Whether the discoverable "sign in with a passkey" control should be shown.</summary>
    public bool IsPasskeyLoginEnabled => Tenant?.Authentication.IsPasskeyLoginEnabled ?? false;

    /// <summary>The external providers to render sign-in buttons for.</summary>
    public IReadOnlyList<ExternalProviderRegistration> ExternalProviders =>
        Tenant?.GetHuiaOpenId()?.External?.Providers ?? [];

    /// <summary>The tenant display name for the UI heading.</summary>
    public string DisplayName => Tenant?.Branding.DisplayName
        ?? Tenant?.DisplayName
        ?? TenantId
        ?? "Huia";

    /// <summary>An error message to render above the form (a resolved string, not a resource key).</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>The request path base (the <c>/{tenant}</c> segment after Finbuckle rebasing).</summary>
    public string PathBase => Request.PathBase.HasValue ? Request.PathBase.Value! : string.Empty;

    /// <summary>
    /// The redirect to follow after an interactive sign-in. Uses <paramref name="returnUrl"/> when it is
    /// a real local path; when it is only the tenant root (a sign-in reached with no OAuth flow in
    /// progress) it sends the user to the tenant's client application instead of the identity provider's
    /// own root, which has nothing to serve.
    /// </summary>
    /// <param name="returnUrl">A return URL already sanitized by <c>IReturnUrlProtector</c>.</param>
    /// <returns>A local redirect to <paramref name="returnUrl"/>, or a redirect to the client home.</returns>
    protected IActionResult ResolvePostAuthRedirect(string returnUrl)
    {
        var tenantRoot = PathBase.Length == 0 ? "/" : PathBase + "/";
        var isTenantRootOnly = string.Equals(returnUrl, tenantRoot, StringComparison.Ordinal)
            || string.Equals(returnUrl, PathBase, StringComparison.Ordinal);

        if (!isTenantRootOnly && Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }

        return TenantClientHome.Resolve(Tenant) is { } clientHome
            ? Redirect(clientHome)
            : LocalRedirect(returnUrl);
    }

    /// <summary>
    /// The redirect to follow after a <em>first</em> interactive sign-in (a sign-up, a phone or external
    /// first sign-in, or the first password sign-in that follows email confirmation). When the tenant
    /// offers passkeys and the account has none, the user is sent to the one-time enrollment
    /// interstitial first; otherwise this is <see cref="ResolvePostAuthRedirect"/>.
    /// </summary>
    /// <param name="user">The account that just signed in.</param>
    /// <param name="returnUrl">A return URL already sanitized by <c>IReturnUrlProtector</c>.</param>
    protected async Task<IActionResult> ResolvePostSignUpRedirectAsync(HuiaUser user, string returnUrl)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (IsPasskeyLoginEnabled
            && HttpContext.RequestServices.GetService<HuiaUserManager>() is { } userManager
            && await userManager.CountPasskeysAsync(user) == 0
            && await userManager.GetAuthenticationTokenAsync(user, HuiaOpenIdConstants.PasskeyLoginProvider, HuiaOpenIdConstants.EnrollPromptedTokenName) is null)
        {
            return RedirectToPage("./PasskeyEnroll", new { returnUrl });
        }

        return ResolvePostAuthRedirect(returnUrl);
    }

    /// <summary>
    /// Feeds the shared layout's branding hooks from the resolved tenant before the page handler runs.
    /// The handler still owns <c>ViewData["Title"]</c> / <c>["Heading"]</c> / <c>["Subtitle"]</c>.
    /// </summary>
    /// <param name="context">The handler-executing context.</param>
    public override void OnPageHandlerExecuting(PageHandlerExecutingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        base.OnPageHandlerExecuting(context);

        var branding = Tenant?.Branding;
        ViewData["Brand"] = DisplayName;
        ViewData["Accent"] = branding?.AccentColor;
        ViewData["LogoUrl"] = branding?.LogoUrl;
        ViewData["FaviconUrl"] = branding?.FaviconUrl;
        ViewData["TermsUrl"] = branding?.TermsUrl?.ToString();
        ViewData["PrivacyUrl"] = branding?.PrivacyUrl?.ToString();
        ViewData["SupportUrl"] = branding?.SupportUrl?.ToString();
    }
}
