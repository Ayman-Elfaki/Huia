namespace Huia.Options;

/// <summary>
/// Abstract base class for an external identity provider (Google, GitHub, Microsoft, OpenID Connect).
/// </summary>
public abstract class ExternalIdentityProvider : IHuiaOptionsSection
{
    /// <summary>The provider registration name/scheme.</summary>
    public string Name { get; set; }

    /// <summary>The client id issued by the external provider.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>The client secret issued by the external provider.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Display name shown on the login button.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Custom redirect URI callback path. Defaults to <c>signin-{Name}</c>.</summary>
    public Uri? RedirectUri { get; set; }

    /// <summary>Custom OAuth/OIDC scopes requested from the provider.</summary>
    public IList<string> Scopes { get; } = [];

    /// <summary>The external provider kind.</summary>
    public abstract ExternalProviderKind Kind { get; }

    /// <summary>Initializes an external identity provider.</summary>
    protected ExternalIdentityProvider(string name)
    {
        Name = name;
    }

    /// <summary>Validates provider configuration.</summary>
    public virtual void Validate(string path, List<string> errors)
    {
        errors.Require(!string.IsNullOrWhiteSpace(Name), HuiaOptionsValidation.Combine(path, nameof(Name)),
            "is required.");
        errors.Require(!string.IsNullOrWhiteSpace(ClientId), HuiaOptionsValidation.Combine(path, nameof(ClientId)),
            "is required.");
        errors.Require(!string.IsNullOrWhiteSpace(ClientSecret), HuiaOptionsValidation.Combine(path, nameof(ClientSecret)),
            "is required.");

        if (RedirectUri is not null)
        {
            errors.Require(!RedirectUri.IsAbsoluteUri, HuiaOptionsValidation.Combine(path, nameof(RedirectUri)),
                "must be a relative URI (e.g. 'signin-google').");
        }
    }

    void IHuiaOptionsSection.Validate(string path, List<string> errors) => Validate(path, errors);
}

/// <summary>Google external login provider.</summary>
public class GoogleExternalProvider : ExternalIdentityProvider
{
    /// <inheritdoc />
    public override ExternalProviderKind Kind => ExternalProviderKind.Google;

    /// <summary>Creates a Google provider with name "Google".</summary>
    public GoogleExternalProvider(string clientId = "", string clientSecret = "") : base("Google")
    {
        ClientId = clientId;
        ClientSecret = clientSecret;
        DisplayName = "Google";
    }
}

/// <summary>GitHub external login provider.</summary>
public class GitHubExternalProvider : ExternalIdentityProvider
{
    /// <inheritdoc />
    public override ExternalProviderKind Kind => ExternalProviderKind.GitHub;

    /// <summary>Creates a GitHub provider with name "GitHub".</summary>
    public GitHubExternalProvider(string clientId = "", string clientSecret = "") : base("GitHub")
    {
        ClientId = clientId;
        ClientSecret = clientSecret;
        DisplayName = "GitHub";
    }
}

/// <summary>Microsoft Account external login provider.</summary>
public class MicrosoftExternalProvider : ExternalIdentityProvider
{
    /// <inheritdoc />
    public override ExternalProviderKind Kind => ExternalProviderKind.MicrosoftAccount;

    /// <summary>Creates a Microsoft provider with name "Microsoft".</summary>
    public MicrosoftExternalProvider(string clientId = "", string clientSecret = "") : base("Microsoft")
    {
        ClientId = clientId;
        ClientSecret = clientSecret;
        DisplayName = "Microsoft";
    }
}

/// <summary>Generic OpenID Connect upstream provider.</summary>
public class OpenIdConnectExternalProvider : ExternalIdentityProvider
{
    /// <inheritdoc />
    public override ExternalProviderKind Kind => ExternalProviderKind.OpenIdConnect;

    /// <summary>The upstream OIDC provider authority URL (e.g. 'https://login.microsoftonline.com/...').</summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>Creates an OpenID Connect provider with scheme name and authority.</summary>
    public OpenIdConnectExternalProvider(string name, string clientId = "", string clientSecret = "", string authority = "") : base(name)
    {
        ClientId = clientId;
        ClientSecret = clientSecret;
        Authority = authority;
    }

    /// <inheritdoc />
    public override void Validate(string path, List<string> errors)
    {
        base.Validate(path, errors);

        errors.Require(!string.IsNullOrWhiteSpace(Authority) && Uri.TryCreate(Authority, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps),
            HuiaOptionsValidation.Combine(path, nameof(Authority)),
            "must be an absolute HTTP or HTTPS URL.");
    }
}
