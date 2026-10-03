namespace Huia.Options;

/// <summary>
/// Abstract base class for all OAuth / OpenID Connect client applications in Huia.
/// </summary>
public abstract class HuiaApplication : IHuiaOptionsSection
{
    /// <summary>The unique client id.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Human-readable name shown on consent and management screens.</summary>
    public string? DisplayName { get; set; }

    /// <summary>The client application's home page.</summary>
    public Uri? ClientUri { get; set; }

    /// <summary>URL of the client application's logo.</summary>
    public Uri? LogoUri { get; set; }

    /// <summary>Whether an explicit consent screen is shown even for first-party clients.</summary>
    public bool RequireConsent { get; set; }

    /// <summary>Scopes the client is permitted to request, beyond the always-granted <c>openid</c>.</summary>
    public IList<string> Scopes { get; } = [];

    /// <summary>Per-client token lifetime overrides.</summary>
    public TokenLifetimeOptions Token { get; set; } = new();

    /// <summary>The client shape, which determines default grant types and endpoint permissions.</summary>
    public virtual ClientKind Kind => ClientKind.ServerSideWebApplication;

    /// <summary>True for public client shapes that never carry a secret.</summary>
    public abstract bool IsPublic { get; }

    /// <summary>Initializes an application with an optional client identifier.</summary>
    protected HuiaApplication(string clientId = "")
    {
        ClientId = clientId;
    }

    /// <summary>Validates the application options.</summary>
    public virtual void Validate(string path, List<string> errors)
    {
        errors.Require(!string.IsNullOrWhiteSpace(ClientId), HuiaOptionsValidation.Combine(path, nameof(ClientId)),
            "is required.");

        var optionalUris = new[] { ClientUri, LogoUri }.Where(u => u is not null).Select(u => u!);
        foreach (var uri in optionalUris)
        {
            errors.Require(uri.IsAbsoluteUri, HuiaOptionsValidation.Combine(path, "Uris"),
                $"'{uri}' must be an absolute URI.");
        }

        ((IHuiaOptionsSection)Token).Validate(HuiaOptionsValidation.Combine(path, nameof(Token)), errors);
    }

    void IHuiaOptionsSection.Validate(string path, List<string> errors) => Validate(path, errors);
}
