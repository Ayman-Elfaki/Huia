namespace Huia.Options;

/// <summary>
/// A public native / mobile / desktop application using authorization code grant with PKCE (no client secret).
/// </summary>
public class NativeApplication : InteractiveClientApplication
{
    /// <inheritdoc />
    public override ClientKind Kind => ClientKind.NativeApplication;

    /// <inheritdoc />
    public override bool IsPublic => true;

    /// <summary>PKCE is always required for public native applications.</summary>
    public bool RequirePkce => true;

    /// <summary>Default constructor.</summary>
    public NativeApplication()
    {
    }

    /// <summary>Creates a native application with client id.</summary>
    public NativeApplication(string clientId) : base(clientId)
    {
    }
}
