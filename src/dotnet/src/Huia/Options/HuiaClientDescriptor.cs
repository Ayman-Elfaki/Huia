namespace Huia.Options;

/// <summary>
/// Declarative description of an OAuth client to seed for a tenant. The seeder translates this into an
/// OpenIddict application, writing the tenant binding into <c>Properties["huia:tenant"]</c> and the home
/// URIs into <c>Properties["huia:home_uris"]</c>.
/// </summary>
public sealed class HuiaClientDescriptor : IHuiaOptionsSection
{
    /// <summary>The <c>client_id</c>. Required and unique within a tenant.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>The client secret. Required for <see cref="ClientKind.ServerSideWebApplication"/> and <see cref="ClientKind.MachineToMachine"/>; must be absent for public clients.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Human-readable name shown on consent and management screens.</summary>
    public string? DisplayName { get; set; }

    /// <summary>The client application's home page. Written to <c>Properties["huia:client_uri"]</c>.</summary>
    public Uri? ClientUri { get; set; }

    /// <summary>URL of the client application's logo. Written to <c>Properties["huia:logo_uri"]</c>.</summary>
    public Uri? LogoUri { get; set; }

    /// <summary>The client shape, which determines the default grant types and endpoint permissions.</summary>
    public ClientKind Kind { get; set; } = ClientKind.ServerSideWebApplication;

    /// <summary>Registered redirect URIs.</summary>
    public IList<Uri> RedirectUris { get; } = [];

    /// <summary>Registered post-logout redirect URIs.</summary>
    public IList<Uri> PostLogoutRedirectUris { get; } = [];

    /// <summary>
    /// "Home" URIs for the client. The first entry is used as the sign-out fall-back target when the
    /// request carries no registered <c>post_logout_redirect_uri</c>.
    /// </summary>
    public IList<Uri> HomeUris { get; } = [];

    /// <summary>Scopes the client is permitted to request, beyond the always-granted <c>openid</c>.</summary>
    public IList<string> Scopes { get; } = [];

    /// <summary>Whether PKCE is required. Forced on for public interactive clients regardless of this value.</summary>
    public bool RequirePkce { get; set; }

    /// <summary>Whether an explicit consent screen is shown even for first-party clients.</summary>
    public bool RequireConsent { get; set; }

    /// <summary>
    /// Whether this client must start authorization with a Pushed Authorization Request
    /// (<c>POST /connect/par</c>): a plain <c>GET /connect/authorize</c> without a <c>request_uri</c> is
    /// then rejected. Off by default; any interactive client <em>may</em> use PAR regardless.
    /// </summary>
    public bool RequirePushedAuthorizationRequests { get; set; }

    /// <summary>Per-client token lifetime overrides.</summary>
    public TokenLifetimeOptions Token { get; set; } = new();

    /// <summary>True for the public interactive shapes that never carry a secret.</summary>
    public bool IsPublic => Kind is ClientKind.SinglePageApplication or ClientKind.NativeApplication;

    /// <summary>
    /// Validates this descriptor on its own (for example when it was built from an admin-API request
    /// rather than the options tree), throwing <see cref="HuiaOptionsException"/> on any problem.
    /// </summary>
    /// <exception cref="HuiaOptionsException">One or more fields are invalid.</exception>
    public void Validate()
    {
        var errors = new List<string>();
        ((IHuiaOptionsSection)this).Validate(nameof(HuiaClientDescriptor), errors);
        if (errors.Count > 0)
        {
            throw new HuiaOptionsException(errors);
        }
    }

    void IHuiaOptionsSection.Validate(string path, List<string> errors)
    {
        errors.Require(!string.IsNullOrWhiteSpace(ClientId), HuiaOptionsValidation.Combine(path, nameof(ClientId)),
            "is required.");

        var interactive = Kind is ClientKind.ServerSideWebApplication or ClientKind.SinglePageApplication
            or ClientKind.NativeApplication;

        if (interactive)
        {
            errors.Require(RedirectUris.Count > 0, HuiaOptionsValidation.Combine(path, nameof(RedirectUris)),
                "must contain at least one URI for an interactive client.");
        }

        if (IsPublic)
        {
            errors.Require(string.IsNullOrEmpty(ClientSecret), HuiaOptionsValidation.Combine(path, nameof(ClientSecret)),
                "must not be set for a public (SPA or native) client.");
        }
        else if (Kind is ClientKind.ServerSideWebApplication or ClientKind.MachineToMachine)
        {
            errors.Require(!string.IsNullOrWhiteSpace(ClientSecret), HuiaOptionsValidation.Combine(path, nameof(ClientSecret)),
                "is required for a confidential client.");
        }

        var optionalUris = new[] { ClientUri, LogoUri }.Where(u => u is not null).Select(u => u!);
        foreach (var uri in RedirectUris.Concat(PostLogoutRedirectUris).Concat(HomeUris).Concat(optionalUris))
        {
            errors.Require(uri.IsAbsoluteUri, HuiaOptionsValidation.Combine(path, "Uris"),
                $"'{uri}' must be an absolute URI.");
        }

        ((IHuiaOptionsSection)Token).Validate(HuiaOptionsValidation.Combine(path, nameof(Token)), errors);
    }
}
