namespace Huia.Applications;

/// <summary>
/// Identity/hosting configuration for the OpenIddict server Huia stands up.
/// </summary>
public sealed class ApplicationsOptions
{
    /// <summary>
    /// The issuer URI OpenIddict advertises in tokens and discovery metadata.
    /// Set via <c>services.AddHuia(issuer, huia => {...})</c>.
    /// </summary>
    public string Issuer { get; }

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

    internal ApplicationsOptions(string issuer) => Issuer = issuer;

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
    /// happens to share a scope name would otherwise still pass. A token's <c>aud</c> claim is only
    /// populated for scopes that have a resource registered against them (see the <c>/admin/scopes</c>
    /// endpoints' <c>Resources</c> field, or <c>OpenIddictScopeDescriptor.Resources</c>) — an audience
    /// listed here needs at least one scope configured with a matching resource, or every token will be
    /// rejected.
    /// </summary>
    public void RequireAudiences(params string[] audiences) => RequiredAudiences = audiences;
}