namespace Huia.Applications;


/// <summary>
/// A public client for input-constrained devices (TVs, CLIs, IoT) using the device authorization flow.
/// Never holds a client secret.
/// </summary>
public sealed class DeviceApplicationOptions : ClientApplicationOptions;

/// <summary>
/// A public, installed client (mobile/desktop app) using the authorization code flow with PKCE and a
/// custom URI scheme or loopback redirect. Never holds a client secret.
/// </summary>
public sealed class NativeApplicationOptions : ClientApplicationOptions;

/// <summary>
/// A confidential, server-rendered web application using the authorization code flow. Holds a client
/// secret; PKCE is optional (enable it with <see cref="RequirePkce"/> for defense in depth).
/// </summary>
public sealed class ServerSideWebApplicationOptions : ConfidentialClientApplicationOptions
{
    /// <summary>Whether PKCE is required for this client's authorization code flow. See <see cref="RequirePkce"/>.</summary>
    public bool RequiresPkce { get; private set; }

    /// <summary>Requires PKCE in addition to the client secret, for defense in depth.</summary>
    public void RequirePkce() => RequiresPkce = true;
}

/// <summary>
/// A confidential, non-interactive client (background service, worker, server-to-server integration)
/// using the client_credentials flow. Holds a client secret; has no redirect URIs.
/// </summary>
public sealed class MachineToMachineApplicationOptions : ConfidentialClientApplicationOptions;


/// <summary>
/// A public, browser-based client (React/Vue/Angular) SPA using the authorization code flow with
/// PKCE. Never holds a client secret.
/// </summary>
public sealed class SinglePageApplicationOptions : ClientApplicationOptions;