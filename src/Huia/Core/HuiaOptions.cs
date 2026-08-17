using Huia.Applications;
using Huia.Branding;
using Huia.Core.Authentication;
using Huia.Keys;
using Huia.Localization;
using Huia.Passwordless;
using Huia.Scheduling;
using Huia.Scopes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Validation.AspNetCore;

namespace Huia.Core;

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
    /// Customizes ASP.NET Core Identity's shared <see cref="IdentityOptions"/> (e.g. <c>identity.Lockout</c>,
    /// <c>identity.Password</c>) — one callback, applying regardless of which sign-in flow(s) are enabled,
    /// since <see cref="IdentityOptions"/> is inherently a single, shared configuration space (lockout policy,
    /// say, isn't meaningfully "per-flow"). Runs directly against the same <see cref="IdentityOptions"/>
    /// instance ASP.NET Core Identity itself builds, so changes here actually take effect — unlike an
    /// even-older, removed <c>HuiaOptions.Identity</c> property that mutated a disconnected
    /// <see cref="IdentityOptions"/> instance <c>AddIdentityCore</c> never read from.
    /// </summary>
    public Action<IdentityOptions>? Identity { get; set; }

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

        // The standard Identity.Application cookie scheme, not a custom one: SignInManager<HuiaUser>'s
        // high-level methods (PasswordSignInAsync, TwoFactorSignInAsync, lockout handling, etc. — all used
        // by Huia's Razor Pages) target IdentityConstants.ApplicationScheme by convention, always passing it
        // explicitly rather than relying on this default. DefaultSignInScheme is instead
        // IdentityConstants.ExternalScheme — the same default plain ASP.NET Core Identity's own AddIdentity
        // uses — so a remote-authentication handler registered via Authentication.UseExternalAuthenticationFlow
        // below, without setting its own SignInScheme, lands in the external cookie
        // SignInManager.GetExternalLoginInfoAsync() reads from, instead of signing directly into the main
        // application cookie unvalidated.
        var providers = services.AddAuthentication(auth =>
        {
            auth.DefaultScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
            auth.DefaultSignInScheme = IdentityConstants.ExternalScheme;
        });
        providers.AddIdentityCookies();

        // Registered unconditionally (cheap — it's just a cookie handler registration) rather than only when
        // huia.Authentication.UsePasswordlessFlow() is called, since HuiaOptions's constructor runs before
        // configure(options) — the consumer's own callback — so whether that flow will be enabled isn't
        // known yet here. It's only ever actually issued by PhoneLoginModel. This is the tamper-proof
        // GET-to-POST phone-number hand-off (see docs/passwordless.md) — a Data-Protection-encrypted+signed
        // cookie, unforgeable client-side, mirroring IdentityConstants.TwoFactorUserIdScheme's existing role
        // for the password-to-2FA hand-off. TempData is deliberately not used for this: the only TempData key
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

        Authentication = new HuiaAuthenticationBuilder(providers);
    }
}