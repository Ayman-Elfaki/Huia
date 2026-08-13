using Huia.Applications;
using Huia.Branding;
using Huia.Keys;
using Huia.Localization;
using Huia.Scopes;
using Microsoft.AspNetCore.Authentication;
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
    /// Configures which cultures Huia.UI's pages and transactional emails are localized into. English
    /// and Arabic (right-to-left) are supported out of the box.
    /// </summary>
    public LocalizationBuilder Localization { get; } = new();

    /// <summary>
    /// Branding for Huia.UI's server-rendered Identity pages.
    /// </summary>
    public BrandingOptions Branding { get; } = new();

    /// <summary>
    /// Customizes ASP.NET Core Identity's <see cref="IdentityOptions"/> Runs after Huia's own defaults, so this can override them.
    /// </summary>
    public IdentityOptions Identity { get; set; } = new();

    /// <summary>
    /// Registers external (third-party) sign-in providers — e.g. <c>huia.ExternalLogins.AddGoogle(google =>
    /// {...})</c>. This is the same <see cref="Microsoft.AspNetCore.Authentication.AuthenticationBuilder"/>
    /// <c>AddHuia</c> itself uses for the <c>Identity.Application</c>/<c>Identity.External</c> cookie schemes,
    /// exposed directly rather than wrapped, so any standard ASP.NET Core remote-authentication handler works
    /// unmodified — call whichever of <c>AddGoogle</c>/<c>AddMicrosoftAccount</c>/<c>AddOpenIdConnect</c>/
    /// <c>AddOAuth</c> fits. Only <c>AddOAuth</c> (a generic OAuth2 handler) ships in the ASP.NET Core shared
    /// framework; the others need their own NuGet package (e.g. <c>dotnet add package
    /// Microsoft.AspNetCore.Authentication.Google</c>). A provider registered here needs no explicit
    /// <c>SignInScheme</c> — <c>AddHuia</c> sets <see cref="Microsoft.AspNetCore.Authentication.AuthenticationOptions.DefaultSignInScheme"/>
    /// to <see cref="Microsoft.AspNetCore.Identity.IdentityConstants.ExternalScheme"/>, the scheme
    /// <c>SignInManager&lt;HuiaUser&gt;.GetExternalLoginInfoAsync()</c> reads from, so every remote handler
    /// lands there by default the same way it would under plain ASP.NET Core Identity. See
    /// docs/external-providers.md.
    /// </summary>
    public AuthenticationBuilder ExternalLogins { get; }

    /// <summary>
    /// Whether an external sign-in whose provider-reported email matches an existing password account may
    /// be linked to it after the user proves ownership by entering that account's password, instead of
    /// always requiring them to sign in first and link from account settings. See
    /// <see cref="EnableExternalLoginPasswordLinking"/>.
    /// </summary>
    internal bool ExternalLoginPasswordLinkingEnabled { get; private set; }

    /// <summary>
    /// Opts into letting an external sign-in with an email that collides with an existing password account
    /// link itself to that account, once the user proves ownership by entering its password (routed through
    /// the same lockout tracking as a normal password sign-in) — rather than Huia's default of always
    /// redirecting them to sign in first and link the provider from account settings afterward. Off by
    /// default: proving password ownership is a reasonable bar, but it does mean a compromised or
    /// unverified provider-reported email could otherwise link an attacker's external identity onto a
    /// victim's account purely by knowing their password already grants full access — the same access a
    /// signed-in "link from settings" flow assumes, so this only meaningfully changes convenience, not the
    /// actual security boundary. See docs/external-providers.md.
    /// </summary>
    public void EnableExternalLoginPasswordLinking() => ExternalLoginPasswordLinkingEnabled = true;

    /// <summary>
    /// Creates a new <see cref="HuiaOptions"/> with <see cref="Applications"/>
    /// wired to <paramref name="issuer"/> and <paramref name="services"/>.
    /// </summary>
    internal HuiaOptions(string issuer, IServiceCollection services)
    {
        Issuer = issuer;
        Applications = new ApplicationsBuilder(services);
        KeysManagement = new KeyManagementBuilder(services);

        // The standard Identity.Application cookie scheme, not a custom one: SignInManager<HuiaUser>'s
        // high-level methods (PasswordSignInAsync, TwoFactorSignInAsync, lockout handling, etc. — all used
        // by Huia's Razor Pages) target IdentityConstants.ApplicationScheme by convention, always passing it
        // explicitly rather than relying on this default. DefaultSignInScheme is instead
        // IdentityConstants.ExternalScheme — the same default plain ASP.NET Core Identity's own AddIdentity
        // uses — so a remote-authentication handler registered via ExternalLogins below, without setting its
        // own SignInScheme, lands in the external cookie SignInManager.GetExternalLoginInfoAsync() reads
        // from, instead of signing directly into the main application cookie unvalidated.
        ExternalLogins = services.AddAuthentication(auth =>
        {
            auth.DefaultScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
            auth.DefaultSignInScheme = IdentityConstants.ExternalScheme;
        });
        ExternalLogins.AddIdentityCookies();
    }
}