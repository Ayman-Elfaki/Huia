namespace Huia.Options;

/// <summary>
/// An input-constrained device application using the OAuth 2.0 device authorization grant (RFC 8628).
/// </summary>
public class DeviceApplication : HuiaApplication
{
    /// <inheritdoc />
    public override ClientKind Kind => ClientKind.Device;

    /// <summary>Optional client secret when registered as a confidential device client.</summary>
    public string? ClientSecret { get; set; }

    /// <inheritdoc />
    public override bool IsPublic => string.IsNullOrEmpty(ClientSecret);

    /// <summary>Whether PKCE is required for the device flow.</summary>
    public bool RequirePkce { get; set; }

    /// <summary>Default constructor.</summary>
    public DeviceApplication()
    {
    }

    /// <summary>Creates a device application with client id and optional client secret.</summary>
    public DeviceApplication(string clientId, string? clientSecret = null) : base(clientId)
    {
        ClientSecret = clientSecret;
    }
}
