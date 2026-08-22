using System.Globalization;
using System.Security.Claims;
using Huia.Applications;
using Huia.Authentication;
using Huia.Branding;
using Huia.Identity;
using Huia.Keys;
using Huia.Localization;
using Huia.Passwordless;
using Huia.Scheduling;
using Huia.Scopes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Validation.AspNetCore;

namespace Huia;

/// <summary>
/// Configuration surface for <c>services.AddHuia(options => {...})</c>.
/// </summary>
public sealed class HuiaOptions
{
    /// <summary>
    /// The issuer URI OpenIddict advertises in tokens and discovery metadata.
    /// Set via <c>services.AddHuia(issuer, huia => {...})</c>.
    /// </summary>
    public string Issuer { get; init; }

    /// <summary>
    /// Where an unauthenticated browser is redirected from <c>/connect/authorize</c> to sign in. Defaults
    /// to <c>/identity/account/login</c>, the page <c>Huia.UI</c> provides; override this if you host your
    /// own login page instead (e.g. in an SPA) — reference it with <c>Huia.UI</c> not installed.
    /// </summary>
    public string LoginPath { get; private set; } = "/identity/account/login";

    /// <summary>
    /// Allows the connect endpoints to be called over plain HTTP. OpenIddict requires HTTPS by default;
    /// only disable this for local development (e.g. gated behind <c>IHostEnvironment.IsDevelopment()</c>) —
    /// never in production.
    /// </summary>
    internal bool InsecureHttpAllowed { get; private set; }

    /// <summary>
    /// Whether access tokens should be issued as plain (unencrypted) JWTs instead of OpenIddict's default
    /// encrypted JWEs. See <see cref="DisableAccessTokenEncryption"/>.
    /// </summary>
    internal bool AccessTokenEncryptionDisabled { get; private set; }

    /// <summary>
    /// Audiences local token validation requires on a token's <c>aud</c> claim. See <see cref="RequireAudiences"/>.
    /// </summary>
    internal IReadOnlyList<string> RequiredAudiences { get; private set; } = [];

    /// <summary>
    /// Whether new accounts can self-register. See <see cref="DisableRegistration"/>.
    /// </summary>
    internal bool RegistrationEnabled { get; private set; } = true;

    /// <summary>
    /// Overrides <see cref="LoginPath"/>.
    /// </summary>
    public void SetLoginPath(string loginPath) => LoginPath = loginPath;

    /// <summary>
    /// Allows the connect endpoints to be called over plain HTTP. See <see cref="InsecureHttpAllowed"/>.
    /// </summary>
    public void AllowInsecureHttp() => InsecureHttpAllowed = true;

    /// <summary>
    /// Issues access tokens as plain (unencrypted) JWTs rather than encrypted JWEs. Useful when a resource
    /// server other than this one needs to inspect an access token's claims directly (e.g. to validate it
    /// without sharing Huia's encryption key) — signing alone still guarantees integrity and authenticity.
    /// </summary>
    public void DisableAccessTokenEncryption() => AccessTokenEncryptionDisabled = true;

    /// <summary>
    /// Requires a validated access token's <c>aud</c> claim to contain at least one of <paramref name="audiences"/>,
    /// rejecting it otherwise. With this unset (the default), local validation checks a token belongs to
    /// <em>some</em> registered client and carries a required scope (<c>RequireScope</c>), but not that it
    /// was issued for <em>this specific resource server</em> — a token minted for an unrelated API that
    /// happens to share a scope name would otherwise still pass.
    /// <remarks>
    /// A token's <c>aud</c> claim is only populated for scopes that have a resource registered against them
    /// (see the <c>OpenIddictScopeDescriptor.Resources</c>) — an audience
    /// listed here needs at least one scope configured with a matching resource, or every token will be rejected
    /// </remarks>
    /// </summary>
    public void RequireAudiences(params string[] audiences) => RequiredAudiences = audiences;

    /// <summary>
    /// Disables self-service account creation: the Register page and Login's "Create account" link become
    /// unreachable, and passwordless phone sign-in from a previously-unseen number is rejected instead of
    /// silently creating a new account. Existing accounts can still sign in through any enabled flow. Useful
    /// for invite-only or admin-provisioned deployments — create accounts directly via
    /// <c>HuiaUserManager</c> instead (e.g. from an admin endpoint or a seeding script).
    /// </summary>
    public void DisableRegistration() => RegistrationEnabled = false;

    /// <summary>
    /// Declaratively register the OAuth/OIDC client applications Huia should seed on startup.
    /// </summary>
    public ApplicationsBuilder Applications { get; }

    /// <summary>
    /// Declaratively register the OAuth/OIDC scopes (and the resources they carry on a token's <c>aud</c>
    /// claim) Huia should seed on startup.
    /// </summary>
    public ScopesBuilder Scopes { get; } = new();

    /// <summary>
    /// Enables and configures Huia's signing/encryption key management (automatic or manual).
    /// </summary>
    public KeyManagementBuilder KeysManagement { get; }

    /// <summary>
    /// Configures the Quartz job that prunes OpenIddict's own orphaned authorizations/tokens — e.g.
    /// <c>huia.Scheduler.SetMinimumAuthorizationLifespan(...)</c>. See <see cref="SchedulerBuilder"/>.
    /// </summary>
    public SchedulerBuilder Scheduler { get; }

    /// <summary>
    /// Configures which cultures Huia.UI's pages and transactional emails are localized into. English
    /// and Arabic (right-to-left) are supported out of the box.
    /// </summary>
    public LocalizationBuilder Localization { get; } = new();

    /// <summary>
    /// Branding for Huia.UI's server-rendered Identity pages.
    /// </summary>
    public BrandingOptions Branding { get; } = new();

    /// <summary>
    /// Configures which sign-in methods this app accepts — email+password, passwordless phone/OTP,
    /// external (third-party) providers, or any combination. Replaces the old, removed
    /// <c>ExternalLogins</c> (<see cref="AuthenticationBuilder"/>) property and
    /// <c>EnableExternalLoginPasswordLinking()</c> method. See <see cref="HuiaAuthenticationBuilder"/>.
    /// </summary>
    public HuiaAuthenticationBuilder Authentication { get; }

    /// <summary>
    /// Customizes Huia's shared <see cref="HuiaIdentityOptions"/> (e.g. <c>identity.Lockout</c>,
    /// <c>identity.Password</c>) — one callback, applying regardless of which sign-in flow(s) are enabled,
    /// since <see cref="HuiaIdentityOptions"/> is inherently a single, shared configuration space (lockout
    /// policy, say, isn't meaningfully "per-flow"). Runs directly against the same <see cref="HuiaIdentityOptions"/>
    /// instance <c>AddHuia</c> itself builds, so changes here actually take effect.
    /// </summary>
    public Action<HuiaIdentityOptions>? Identity { get; set; }

    /// <summary>
    /// Creates a new <see cref="HuiaOptions"/> with <see cref="Applications"/>
    /// wired to <paramref name="issuer"/> and <paramref name="services"/>.
    /// </summary>
    internal HuiaOptions(string issuer, IServiceCollection services)
    {
        Issuer = issuer;
        Applications = new ApplicationsBuilder(services);
        KeysManagement = new KeyManagementBuilder(services);
        Scheduler = new SchedulerBuilder(services);

        // HuiaAuthenticationDefaults.ApplicationScheme, not a custom one: HuiaSignInManager's high-level
        // methods (PasswordSignInAsync, TwoFactorAuthenticatorSignInAsync, lockout handling, etc. — all used
        // by Huia's Razor Pages) target it by convention, always passing it explicitly rather than relying on
        // this default. DefaultSignInScheme is instead HuiaAuthenticationDefaults.ExternalScheme — so the
        // external-login callback bridge (see Endpoints/ExternalLoginCallbackEndpoints.cs), which signs the
        // OpenIddict client's result explicitly into that scheme, lands in the same external cookie
        // HuiaSignInManager.GetExternalLoginInfoAsync() reads from.
        var providers = services.AddAuthentication(auth =>
        {
            auth.DefaultScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
            auth.DefaultSignInScheme = HuiaAuthenticationDefaults.ExternalScheme;
        });

        AddHuiaCookies(providers);

        Authentication = new HuiaAuthenticationBuilder();
    }

    // Base cookie handler registrations, replicating the paths/expiration defaults ASP.NET Core Identity's
    // own AddIdentityCookies() used to supply implicitly. LoginPath/AccessDeniedPath and the Events not set
    // here (OnRedirectToAccessDenied/OnRedirectToLogin) are configured later, in
    // ServiceCollectionExtensions.AddHuia — after configure(options) (the consumer's own callback) has run,
    // so LoginPath there can reflect a huia.SetLoginPath(...) call. OnValidatePrincipal is set here instead
    // since it doesn't depend on anything configure(options) might change.
    // One line over MA0051's default 60-line limit; splitting this flat sequence of four independent
    // AddCookie registrations wouldn't reduce its actual complexity, just spread it across an extra method.
#pragma warning disable MA0051
    private static void AddHuiaCookies(AuthenticationBuilder providers)
    {
        providers.AddCookie(HuiaAuthenticationDefaults.ApplicationScheme, cookie =>
        {
            cookie.Cookie.HttpOnly = true;
            cookie.Cookie.SameSite = SameSiteMode.Lax;
            cookie.AccessDeniedPath = "/identity/account/accessdenied";

            // Huia's own equivalent of ASP.NET Core Identity's implicit security-stamp cookie revalidation
            // (see the risk this guards against in HuiaSignInManager's own doc comments): without this, a
            // password change or forced sign-out would never actually invalidate an already-issued session
            // cookie, since nothing else re-checks HuiaUser.SecurityStamp against what's embedded in the
            // cookie after it's issued.
            cookie.Events.OnValidatePrincipal = ValidateSecurityStampAsync;
        });

        // This is the tamper-proof hand-off cookie for an in-progress external sign-in — Data-Protection
        // encrypted+signed, unforgeable client-side — mirroring HuiaAuthenticationDefaults.TwoFactorUserIdScheme's
        // role for the password-to-2FA hand-off.
        providers.AddCookie(HuiaAuthenticationDefaults.ExternalScheme, cookie =>
        {
            cookie.Cookie.Name = HuiaAuthenticationDefaults.ExternalScheme;
            cookie.Cookie.HttpOnly = true;
            cookie.Cookie.SameSite = SameSiteMode.Lax;
            cookie.ExpireTimeSpan = TimeSpan.FromMinutes(5);
            cookie.SlidingExpiration = false;
        });

        providers.AddCookie(HuiaAuthenticationDefaults.TwoFactorUserIdScheme, cookie =>
        {
            cookie.Cookie.Name = HuiaAuthenticationDefaults.TwoFactorUserIdScheme;
            cookie.Cookie.HttpOnly = true;
            cookie.Cookie.SameSite = SameSiteMode.Lax;
            cookie.ExpireTimeSpan = TimeSpan.FromMinutes(5);
            cookie.SlidingExpiration = false;
        });

        providers.AddCookie(HuiaAuthenticationDefaults.TwoFactorRememberMeScheme, cookie =>
        {
            cookie.Cookie.Name = HuiaAuthenticationDefaults.TwoFactorRememberMeScheme;
            cookie.Cookie.HttpOnly = true;
            cookie.Cookie.SameSite = SameSiteMode.Lax;
            cookie.ExpireTimeSpan = TimeSpan.FromDays(30);
        });

        // Registered unconditionally (cheap — it's just a cookie handler registration) rather than only when
        // huia.Authentication.UsePasswordlessFlow() is called, since HuiaOptions's constructor runs before
        // configure(options) — the consumer's own callback — so whether that flow will be enabled isn't
        // known yet here. It's only ever actually issued by PhoneLoginModel. This is the tamper-proof
        // GET-to-POST phone-number hand-off (see docs/passwordless.md) — a Data-Protection-encrypted+signed
        // cookie, unforgeable client-side. TempData is deliberately not used for this: the only TempData key
        // Huia's own pages use (ExternalLoginError) carries just a display string, never security-sensitive
        // state, and this stays consistent with that.
        providers.AddCookie(PhoneVerificationScheme.Name, cookie =>
        {
            cookie.Cookie.Name = PhoneVerificationScheme.Name;
            cookie.Cookie.HttpOnly = true;
            cookie.Cookie.SameSite = SameSiteMode.Strict;
            cookie.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            cookie.ExpireTimeSpan = TimeSpan.FromMinutes(5);
            cookie.SlidingExpiration = false;
        });
    }
#pragma warning restore MA0051

    /// <summary>
    /// Re-checks the application cookie's embedded security-stamp claim against the user's current
    /// <see cref="HuiaUser.SecurityStamp"/> — but only once every <see cref="HuiaIdentityOptions.SecurityStampValidationInterval"/>
    /// (tracked via a timestamp stashed in the cookie's own <see cref="AuthenticationProperties"/>), so this
    /// doesn't hit the user store on every single request. A mismatch (security stamp changed — e.g. a
    /// password change — or the user no longer exists) rejects the principal and signs the cookie out
    /// immediately.
    /// </summary>
    private static async Task ValidateSecurityStampAsync(CookieValidatePrincipalContext context)
    {
        const string lastValidatedKey = ".Huia.SecurityStampLastValidated";

        var services = context.HttpContext.RequestServices;
        var identityOptions = services.GetRequiredService<IOptions<HuiaIdentityOptions>>().Value;

        var now = DateTimeOffset.UtcNow;
        if (context.Properties.Items.TryGetValue(lastValidatedKey, out var lastValidatedText) &&
            DateTimeOffset.TryParseExact(lastValidatedText, "O", CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var lastValidated) &&
            now - lastValidated < identityOptions.SecurityStampValidationInterval)
        {
            return;
        }

        var userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var userManager = services.GetRequiredService<HuiaUserManager>();
        var user = userId is null ? null : await userManager.FindByIdAsync(userId).ConfigureAwait(false);
        var stampClaim = context.Principal?.FindFirstValue(HuiaSignInManager.SecurityStampClaimType);

        if (user is null || !string.Equals(stampClaim, user.SecurityStamp, StringComparison.Ordinal))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(HuiaAuthenticationDefaults.ApplicationScheme)
                .ConfigureAwait(false);
            return;
        }

        context.Properties.Items[lastValidatedKey] = now.ToString("O", CultureInfo.InvariantCulture);
        context.ShouldRenew = true;
    }
}
