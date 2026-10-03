namespace Huia.Options;

/// <summary>
/// A public browser single-page application using authorization code grant with PKCE (no client secret).
/// </summary>
public class SinglePageApplication : InteractiveClientApplication
{
    /// <inheritdoc />
    public override ClientKind Kind => ClientKind.SinglePageApplication;

    /// <inheritdoc />
    public override bool IsPublic => true;

    /// <summary>PKCE is always required for public SPAs.</summary>
    public bool RequirePkce => true;

    /// <summary>Default constructor.</summary>
    public SinglePageApplication()
    {
    }

    /// <summary>Creates a single-page application with client id.</summary>
    public SinglePageApplication(string clientId) : base(clientId)
    {
    }
}
