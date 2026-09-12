namespace Huia.Options;

/// <summary>
/// The external identity providers wired for a tenant. <c>Huia.OpenId</c> implements this exclusively
/// through the OpenIddict client; <c>Huia.Headless</c> implements it through the classic ASP.NET Core
/// authentication handlers (<c>AddGoogle</c>, <c>AddMicrosoftAccount</c>, a generic <c>AddOAuth</c> for
/// GitHub, <c>AddOpenIdConnect</c>) instead, since it has no OpenIddict dependency — the registration
/// data here (provider kind, client id/secret, authority, scopes) is shared by both. Enabled via
/// <see cref="HuiaTenantAuthenticationOptions.UseExternalLogin"/>.
/// </summary>
public sealed class ExternalLoginOptions : IHuiaOptionsSection
{
    private readonly List<ExternalProviderRegistration> _providers = [];
    private readonly List<string> _allowedReturnUrlPrefixes = [];

    /// <summary>The registered providers, in declaration order.</summary>
    public IReadOnlyList<ExternalProviderRegistration> Providers => _providers;

    /// <summary>
    /// <c>Huia.Headless</c> only. The browser starts an external-login challenge on a different origin
    /// than it ends on (the app's own frontend, not Huia itself), so the caller-supplied
    /// <c>returnUrl</c> on a challenge request must be validated against a trusted allow-list rather
    /// than the same-origin check <c>Huia.OpenId</c> uses — an unvalidated <c>returnUrl</c> here would be
    /// an open redirect. Each entry is an absolute URL prefix (for example
    /// <c>https://shop.example.com/</c>); <c>Huia.Headless</c> throws at start-up if external login is
    /// enabled with this left empty. Ignored by <c>Huia.OpenId</c>.
    /// </summary>
    public IReadOnlyList<string> AllowedReturnUrlPrefixes => _allowedReturnUrlPrefixes;

    /// <summary>Adds a trusted <c>returnUrl</c> prefix. See <see cref="AllowedReturnUrlPrefixes"/>.</summary>
    /// <param name="prefix">An absolute URL prefix, for example <c>https://shop.example.com/</c>.</param>
    /// <returns>This instance, for chaining.</returns>
    public ExternalLoginOptions AllowReturnUrlPrefix(string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        _allowedReturnUrlPrefixes.Add(prefix);
        return this;
    }

    /// <summary>
    /// Whether a logged-out external sign-in whose email matches an existing local account should be
    /// linked to that account (rather than starting a new sign-up). Off by default. Even when on, the
    /// link is only made when the local account's email is confirmed and the provider vouches for the
    /// address (<c>email_verified</c> is not <c>false</c>); otherwise the sign-in is refused with an
    /// "email already registered" message and no account is created.
    /// </summary>
    public bool AccountLinkingEnabled { get; set; }

    /// <summary>Enables <see cref="AccountLinkingEnabled"/>.</summary>
    /// <returns>This instance, for chaining.</returns>
    public ExternalLoginOptions EnableAccountsLinking()
    {
        AccountLinkingEnabled = true;
        return this;
    }

    /// <summary>Registers Google as an external provider (via the OpenIddict web-integration provider).</summary>
    /// <param name="clientId">The Google OAuth client id.</param>
    /// <param name="clientSecret">The Google OAuth client secret.</param>
    /// <param name="configure">Optional further configuration (extra scopes, display name).</param>
    /// <returns>This instance, for chaining.</returns>
    public ExternalLoginOptions AddGoogle(string clientId, string clientSecret, Action<ExternalProviderRegistration>? configure = null)
        => Add(ExternalProviderKind.Google, "Google", clientId, clientSecret, authority: null, configure);

    /// <summary>Registers GitHub as an external provider (via the OpenIddict web-integration provider).</summary>
    /// <param name="clientId">The GitHub OAuth client id.</param>
    /// <param name="clientSecret">The GitHub OAuth client secret.</param>
    /// <param name="configure">Optional further configuration (extra scopes, display name).</param>
    /// <returns>This instance, for chaining.</returns>
    public ExternalLoginOptions AddGitHub(string clientId, string clientSecret, Action<ExternalProviderRegistration>? configure = null)
        => Add(ExternalProviderKind.GitHub, "GitHub", clientId, clientSecret, authority: null, configure);

    /// <summary>Registers a Microsoft account as an external provider (via the OpenIddict web-integration provider).</summary>
    /// <param name="clientId">The Microsoft application (client) id.</param>
    /// <param name="clientSecret">The Microsoft application client secret.</param>
    /// <param name="configure">Optional further configuration (extra scopes, display name).</param>
    /// <returns>This instance, for chaining.</returns>
    public ExternalLoginOptions AddMicrosoftAccount(string clientId, string clientSecret, Action<ExternalProviderRegistration>? configure = null)
        => Add(ExternalProviderKind.MicrosoftAccount, "MicrosoftAccount", clientId, clientSecret, authority: null, configure);

    /// <summary>Registers a generic OpenID Connect provider by authority URL.</summary>
    /// <param name="name">A stable, unique name for this provider (used in the registration id and callback path).</param>
    /// <param name="clientId">The client id issued by the upstream provider.</param>
    /// <param name="clientSecret">The client secret issued by the upstream provider.</param>
    /// <param name="authority">The provider's issuer / authority URL.</param>
    /// <param name="configure">Optional further configuration (extra scopes, display name).</param>
    /// <returns>This instance, for chaining.</returns>
    public ExternalLoginOptions AddOpenIdConnect(string name, string clientId, string clientSecret, string authority, Action<ExternalProviderRegistration>? configure = null)
        => Add(ExternalProviderKind.OpenIdConnect, name, clientId, clientSecret, authority, configure);

    /// <summary>Registers a fully pre-built provider registration.</summary>
    /// <param name="registration">The registration to add.</param>
    /// <returns>This instance, for chaining.</returns>
    public ExternalLoginOptions AddExternalProvider(ExternalProviderRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        _providers.Add(registration);
        return this;
    }

    private ExternalLoginOptions Add(ExternalProviderKind kind, string name, string clientId, string clientSecret, string? authority, Action<ExternalProviderRegistration>? configure)
    {
        var registration = new ExternalProviderRegistration
        {
            Kind = kind,
            Name = name,
            ClientId = clientId,
            ClientSecret = clientSecret,
            Authority = authority,
        };
        configure?.Invoke(registration);
        _providers.Add(registration);
        return this;
    }

    void IHuiaOptionsSection.Validate(string path, List<string> errors)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < _providers.Count; i++)
        {
            var providerPath = HuiaOptionsValidation.Combine(path, $"Providers[{i}]");
            ((IHuiaOptionsSection)_providers[i]).Validate(providerPath, errors);
            errors.Require(seen.Add(_providers[i].Name), providerPath, $"provider name '{_providers[i].Name}' is duplicated.");
        }
    }
}

/// <summary>A single external identity provider registration.</summary>
public sealed class ExternalProviderRegistration : IHuiaOptionsSection
{
    /// <summary>The protocol / vendor this provider speaks.</summary>
    public ExternalProviderKind Kind { get; set; } = ExternalProviderKind.OpenIdConnect;

    /// <summary>Stable, unique name. Used to build the OpenIddict registration id (<c>{tenant}:{name}</c>) and the <c>/signin-{name}</c> callback path.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional friendly label for the sign-in button. Defaults to <see cref="Name"/>.</summary>
    public string? DisplayName { get; set; }

    /// <summary>The upstream client id.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>The upstream client secret.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>The issuer / authority URL. Required for <see cref="ExternalProviderKind.OpenIdConnect"/>; ignored for the built-in vendors.</summary>
    public string? Authority { get; set; }

    /// <summary>
    /// Additional scopes to request beyond <c>openid</c>. Left empty deliberately by default so a caller
    /// chooses between a minimal and a full-profile request.
    /// </summary>
    public IList<string> Scopes { get; } = [];

    void IHuiaOptionsSection.Validate(string path, List<string> errors)
    {
        errors.Require(!string.IsNullOrWhiteSpace(Name), HuiaOptionsValidation.Combine(path, nameof(Name)), "is required.");
        errors.Require(!string.IsNullOrWhiteSpace(ClientId), HuiaOptionsValidation.Combine(path, nameof(ClientId)), "is required.");
        errors.Require(!string.IsNullOrWhiteSpace(ClientSecret), HuiaOptionsValidation.Combine(path, nameof(ClientSecret)), "is required.");

        if (Kind is ExternalProviderKind.OpenIdConnect)
        {
            errors.Require(Uri.TryCreate(Authority, UriKind.Absolute, out _),
                HuiaOptionsValidation.Combine(path, nameof(Authority)),
                "must be an absolute URL for a generic OpenID Connect provider.");
        }
    }
}
