namespace Huia.Applications;

/// <summary>
/// Base configuration shared by every client-application type Huia can register with OpenIddict.
/// </summary>
public abstract class ClientApplicationOptions
{
    private readonly List<string> _redirectUris = [];
    private readonly List<string> _postLogoutRedirectUris = [];
    private readonly List<string> _scopes = [];

    /// <summary>
    /// The OpenIddict client_id. Required.
    /// </summary>
    public string ClientId { get; private set; } = string.Empty;

    /// <summary>
    /// Human-readable name shown during consent.
    /// Defaults to <see cref="ClientId"/> when unset.
    /// </summary>
    public string? DisplayName { get; private set; }

    /// <summary>
    /// Redirect URIs registered via <see cref="AddRedirectUri"/>.
    /// </summary>
    public IReadOnlyList<string> RedirectUris => _redirectUris;

    /// <summary>
    /// Post-logout redirect URIs registered via <see cref="AddPostLogoutRedirectUri"/>.
    /// </summary>
    public IReadOnlyList<string> PostLogoutRedirectUris => _postLogoutRedirectUris;

    /// <summary>
    /// Scopes this client is allowed to request, registered via <see cref="AllowScopes"/>.
    /// </summary>
    public IReadOnlyList<string> Scopes => _scopes;

    /// <summary>
    /// The client's OpenID Connect front-channel logout URI, set via <see cref="SetFrontChannelLogoutUri"/>.
    /// When set, Huia's <c>/connect/logout</c> loads this URL in a hidden browser iframe (with an
    /// <c>iss</c> query parameter) alongside any other signed-in client's, so it can clear its own session
    /// when the user signs out anywhere.
    /// </summary>
    public string? FrontChannelLogoutUri { get; private set; }

    /// <summary>
    /// The client's OpenID Connect back-channel logout URI, set via <see cref="SetBackChannelLogoutUri"/>.
    /// When set, Huia's <c>/connect/logout</c> POSTs a signed <c>logout_token</c> directly to this URL
    /// (server-to-server, no browser involved) so the client can end the user's session there even if its
    /// front-channel logout iframe never loads.
    /// </summary>
    public string? BackChannelLogoutUri { get; private set; }

    /// <summary>
    /// Where to send the browser back to this client when there's no in-flight OAuth request to resume — set
    /// via <see cref="SetHomeUri"/>. Two places prefer this
    /// (see <c>Huia.Helpers.ClientHomeResolver</c>) when set, falling back to just the origin of a registered
    /// redirect_uri otherwise: an email confirmation/password reset link clicked out of band, after whatever
    /// originally triggered it has already completed or gone stale; and the branded error page's "return
    /// home" link, when the failing request's <c>client_id</c> can still be identified (e.g. an unknown
    /// <c>redirect_uri</c>).
    /// </summary>
    public string? HomeUri { get; private set; }

    /// <summary>
    /// When <c>true</c> (the default), consent is auto-granted for this client instead of requiring an
    /// explicit consent screen. Set to <c>false</c> for third-party clients.
    /// </summary>
    public bool IsTrusted { get; private set; } = true;

    /// <summary>
    /// Overrides the server-wide access token lifetime for this client only, set via
    /// <see cref="SetAccessTokenLifetime"/>. Unset (the default) falls back to the server-wide lifetime.
    /// </summary>
    public TimeSpan? AccessTokenLifetime { get; private set; }

    /// <summary>
    /// Overrides the server-wide identity token lifetime for this client only, set via
    /// <see cref="SetIdentityTokenLifetime"/>. Unset (the default) falls back to the server-wide lifetime.
    /// </summary>
    public TimeSpan? IdentityTokenLifetime { get; private set; }

    /// <summary>
    /// Overrides the server-wide refresh token lifetime for this client only, set via
    /// <see cref="SetRefreshTokenLifetime"/>. Unset (the default) falls back to the server-wide lifetime.
    /// </summary>
    public TimeSpan? RefreshTokenLifetime { get; private set; }

    /// <summary>
    /// Sets <see cref="ClientId"/>. Required for every client application.
    /// </summary>
    public void SetClientId(string clientId) => ClientId = clientId;

    /// <summary>
    /// Sets <see cref="DisplayName"/>.
    /// </summary>
    public void SetDisplayName(string displayName) => DisplayName = displayName;

    /// <summary>
    /// Marks this client untrusted, requiring explicit user consent (see <see cref="IsTrusted"/>).
    /// </summary>
    public void RequireConsent() => IsTrusted = false;

    /// <summary>
    /// Adds an allowed redirect URI for the authorization code flow.
    /// </summary>
    public void AddRedirectUri(string uri) => _redirectUris.Add(uri);

    /// <summary>
    /// Adds an allowed redirect URI for RP-initiated logout (<c>/connect/logout</c>).
    /// </summary>
    public void AddPostLogoutRedirectUri(string uri) => _postLogoutRedirectUris.Add(uri);

    /// <summary>
    /// Adds scopes this client is allowed to request.
    /// </summary>
    public void AllowScopes(params string[] scopes) => _scopes.AddRange(scopes);

    /// <summary>
    /// Sets <see cref="FrontChannelLogoutUri"/>.
    /// </summary>
    public void SetFrontChannelLogoutUri(string uri) => FrontChannelLogoutUri = uri;

    /// <summary>
    /// Sets <see cref="BackChannelLogoutUri"/>.
    /// </summary>
    public void SetBackChannelLogoutUri(string uri) => BackChannelLogoutUri = uri;

    /// <summary>
    /// Sets <see cref="HomeUri"/>.
    /// </summary>
    public void SetHomeUri(string uri) => HomeUri = uri;

    /// <summary>
    /// Overrides the server-wide access token lifetime for this client only.
    /// </summary>
    public void SetAccessTokenLifetime(TimeSpan lifetime) => AccessTokenLifetime = lifetime;

    /// <summary>
    /// Overrides the server-wide identity token lifetime for this client only.
    /// </summary>
    public void SetIdentityTokenLifetime(TimeSpan lifetime) => IdentityTokenLifetime = lifetime;

    /// <summary>
    /// Overrides the server-wide refresh token lifetime for this client only.
    /// </summary>
    public void SetRefreshTokenLifetime(TimeSpan lifetime) => RefreshTokenLifetime = lifetime;

    internal virtual void Validate(string applicationKind)
    {
        if (string.IsNullOrWhiteSpace(ClientId))
        {
            throw new InvalidOperationException(
                $"A {applicationKind} application was registered without a client ID. Call SetClientId(...) inside the configuration callback.");
        }
    }
}