namespace Huia.Options;

/// <summary>
/// A confidential server-rendered web application using authorization code grant and client secret.
/// </summary>
public class ServerSideWebApplication : InteractiveClientApplication
{
    /// <inheritdoc />
    public override ClientKind Kind => ClientKind.ServerSideWebApplication;

    /// <inheritdoc />
    public override bool IsPublic => false;

    /// <summary>The client secret required for confidential token requests.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Whether PKCE is required (defaults to true for heightened security).</summary>
    public bool RequirePkce { get; set; } = true;

    /// <summary>
    /// Whether this client must start authorization with a Pushed Authorization Request (PAR, RFC 9126).
    /// </summary>
    public bool RequiresPushedAuthorizationRequests { get; set; }

    /// <summary>Default constructor.</summary>
    public ServerSideWebApplication()
    {
    }

    /// <summary>Creates a server-side web application with client id and secret.</summary>
    public ServerSideWebApplication(string clientId, string clientSecret) : base(clientId)
    {
        ClientSecret = clientSecret;
    }

    /// <summary>Requires this client to use Pushed Authorization Requests.</summary>
    /// <returns>This instance, for chaining.</returns>
    public ServerSideWebApplication RequirePushedAuthorizationRequests()
    {
        RequiresPushedAuthorizationRequests = true;
        return this;
    }

    /// <inheritdoc />
    public override void Validate(string path, List<string> errors)
    {
        base.Validate(path, errors);

        errors.Require(!string.IsNullOrWhiteSpace(ClientSecret),
            HuiaOptionsValidation.Combine(path, nameof(ClientSecret)),
            "is required for a confidential client.");
    }
}
