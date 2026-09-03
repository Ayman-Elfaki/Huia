namespace Huia.Options;

/// <summary>
/// The external identity providers wired for a tenant. External login is implemented exclusively through
/// the OpenIddict client (never the classic ASP.NET Core authentication handlers). Enabled via
/// <see cref="PasswordlessFlowOptions.UseExternalLogin"/>.
/// </summary>
public sealed class ExternalLoginOptions : IHuiaOptionsSection
{
    private readonly List<ExternalProviderRegistration> _providers = [];

    /// <summary>The registered providers, in declaration order.</summary>
    public IReadOnlyList<ExternalProviderRegistration> Providers => _providers;

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
