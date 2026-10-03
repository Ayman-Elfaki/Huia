namespace Huia.Options;

/// <summary>
/// Federated external login authentication method options.
/// </summary>
public class ExternalLoginAuthenticationMethod : HuiaAuthenticationMethod
{
    /// <inheritdoc />
    public override string MethodType => "External";

    /// <summary>Configured external identity providers.</summary>
    public IList<ExternalIdentityProvider> Providers { get; } = [];

    /// <summary>Whether external logins are automatically linked to accounts with confirmed matching email.</summary>
    public bool AccountLinkingEnabled { get; set; } = false;

    /// <summary>Enables linking external logins to existing accounts with matching confirmed email.</summary>
    public ExternalLoginAuthenticationMethod EnableAccountsLinking()
    {
        AccountLinkingEnabled = true;
        return this;
    }

    /// <summary>Origins allowed for return URLs in external login challenges.</summary>
    public IList<string> AllowedReturnUrlPrefixes { get; } = [];

    /// <summary>Registers an allowed prefix for return URLs.</summary>
    public ExternalLoginAuthenticationMethod AllowReturnUrlPrefix(string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        AllowedReturnUrlPrefixes.Add(prefix);
        return this;
    }

    /// <summary>Adds a Google login provider.</summary>
    public ExternalLoginAuthenticationMethod AddGoogle(string clientId, string clientSecret, Action<GoogleExternalProvider>? configure = null)
    {
        var provider = new GoogleExternalProvider(clientId, clientSecret);
        configure?.Invoke(provider);
        Providers.Add(provider);
        return this;
    }

    /// <summary>Adds a GitHub login provider.</summary>
    public ExternalLoginAuthenticationMethod AddGitHub(string clientId, string clientSecret, Action<GitHubExternalProvider>? configure = null)
    {
        var provider = new GitHubExternalProvider(clientId, clientSecret);
        configure?.Invoke(provider);
        Providers.Add(provider);
        return this;
    }

    /// <summary>Adds a Microsoft login provider.</summary>
    public ExternalLoginAuthenticationMethod AddMicrosoft(string clientId, string clientSecret, Action<MicrosoftExternalProvider>? configure = null)
    {
        var provider = new MicrosoftExternalProvider(clientId, clientSecret);
        configure?.Invoke(provider);
        Providers.Add(provider);
        return this;
    }

    /// <summary>Adds a generic OpenID Connect login provider.</summary>
    public ExternalLoginAuthenticationMethod AddOpenIdConnect(string name, string clientId, string clientSecret, string authority, Action<OpenIdConnectExternalProvider>? configure = null)
    {
        var provider = new OpenIdConnectExternalProvider(name, clientId, clientSecret, authority);
        configure?.Invoke(provider);
        Providers.Add(provider);
        return this;
    }

    /// <inheritdoc />
    public override void Validate(string path, List<string> errors)
    {
        base.Validate(path, errors);

        errors.Require(Providers.Count > 0, path, "at least one external provider must be configured.");

        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < Providers.Count; i++)
        {
            var provider = Providers[i];
            var providerPath = HuiaOptionsValidation.Combine(path, $"Providers[{i}]");
            ((IHuiaOptionsSection)provider).Validate(providerPath, errors);

            if (!string.IsNullOrWhiteSpace(provider.Name))
            {
                errors.Require(seenNames.Add(provider.Name), providerPath,
                    $"external provider name '{provider.Name}' is duplicated in this tenant.");
            }
        }

        for (var i = 0; i < AllowedReturnUrlPrefixes.Count; i++)
        {
            var prefix = AllowedReturnUrlPrefixes[i];
            var prefixPath = HuiaOptionsValidation.Combine(path, $"{nameof(AllowedReturnUrlPrefixes)}[{i}]");
            errors.Require(!string.IsNullOrWhiteSpace(prefix) && Uri.TryCreate(prefix, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps),
                prefixPath, $"'{prefix}' must be an absolute HTTP or HTTPS URL.");
        }
    }
}
