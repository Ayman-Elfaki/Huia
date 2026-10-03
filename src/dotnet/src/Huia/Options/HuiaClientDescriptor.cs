namespace Huia.Options;

/// <summary>
/// Declarative description of an OAuth client to seed for a tenant.
/// Inherits from <see cref="InteractiveClientApplication"/> as part of the <see cref="HuiaApplication"/> hierarchy.
/// </summary>
public class HuiaClientDescriptor : InteractiveClientApplication
{
    private ClientKind _kind = ClientKind.ServerSideWebApplication;

    /// <summary>The client secret. Required for confidential clients; must be absent for public clients.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>The client shape, which determines default grant types and endpoint permissions.</summary>
    public new ClientKind Kind
    {
        get => _kind;
        set => _kind = value;
    }

    /// <summary>Whether PKCE is required.</summary>
    public bool RequirePkce { get; set; }

    /// <summary>Whether this client requires Pushed Authorization Requests (RFC 9126).</summary>
    public bool RequiresPushedAuthorizationRequests { get; set; }

    /// <inheritdoc />
    public override bool IsPublic => Kind is ClientKind.SinglePageApplication or ClientKind.NativeApplication;

    /// <summary>Default constructor.</summary>
    public HuiaClientDescriptor()
    {
    }

    /// <summary>Creates a client descriptor with an id and kind.</summary>
    public HuiaClientDescriptor(string clientId, ClientKind kind = ClientKind.ServerSideWebApplication) : base(clientId)
    {
        _kind = kind;
        if (IsPublic)
        {
            RequirePkce = true;
        }
    }

    /// <summary>Requires this client to start authorization with a Pushed Authorization Request.</summary>
    /// <returns>This instance, for chaining.</returns>
    public HuiaClientDescriptor RequirePushedAuthorizationRequests()
    {
        RequiresPushedAuthorizationRequests = true;
        return this;
    }

    /// <summary>Validates this descriptor on its own, throwing <see cref="HuiaOptionsException"/> on any error.</summary>
    public void Validate()
    {
        var errors = new List<string>();
        ((IHuiaOptionsSection)this).Validate(nameof(HuiaClientDescriptor), errors);
        if (errors.Count > 0)
        {
            throw new HuiaOptionsException(errors);
        }
    }

    /// <inheritdoc />
    public override void Validate(string path, List<string> errors)
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
