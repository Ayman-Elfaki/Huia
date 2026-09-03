namespace Huia.Options;

/// <summary>Factory methods for the supported OAuth client shapes. Each adds a <see cref="HuiaClientDescriptor"/> to a tenant.</summary>
public static class HuiaClientFactories
{
    /// <summary>Adds a confidential server-rendered web application (authorization code + client secret).</summary>
    /// <param name="tenant">The tenant to add the client to.</param>
    /// <param name="clientId">The client id.</param>
    /// <param name="clientSecret">The client secret.</param>
    /// <param name="configure">Further configuration (redirect URIs, scopes, token lifetimes).</param>
    /// <returns>The new descriptor.</returns>
    public static HuiaClientDescriptor AddServerSideWebApplication(
        this TenantOptions tenant, string clientId, string clientSecret, Action<HuiaClientDescriptor>? configure = null) =>
        Add(tenant, clientId, ClientKind.ServerSideWebApplication, d => { d.ClientSecret = clientSecret; configure?.Invoke(d); });

    /// <summary>Adds a public browser single-page application (authorization code + PKCE, no secret).</summary>
    /// <param name="tenant">The tenant to add the client to.</param>
    /// <param name="clientId">The client id.</param>
    /// <param name="configure">Further configuration.</param>
    /// <returns>The new descriptor.</returns>
    public static HuiaClientDescriptor AddSinglePageApplication(
        this TenantOptions tenant, string clientId, Action<HuiaClientDescriptor>? configure = null) =>
        Add(tenant, clientId, ClientKind.SinglePageApplication, d => { d.RequirePkce = true; configure?.Invoke(d); });

    /// <summary>Adds a public native / mobile / desktop application (authorization code + PKCE, no secret).</summary>
    /// <param name="tenant">The tenant to add the client to.</param>
    /// <param name="clientId">The client id.</param>
    /// <param name="configure">Further configuration.</param>
    /// <returns>The new descriptor.</returns>
    public static HuiaClientDescriptor AddNativeApplication(
        this TenantOptions tenant, string clientId, Action<HuiaClientDescriptor>? configure = null) =>
        Add(tenant, clientId, ClientKind.NativeApplication, d => { d.RequirePkce = true; configure?.Invoke(d); });

    /// <summary>Adds an input-constrained device client (device authorization grant).</summary>
    /// <param name="tenant">The tenant to add the client to.</param>
    /// <param name="clientId">The client id.</param>
    /// <param name="configure">Further configuration.</param>
    /// <returns>The new descriptor.</returns>
    public static HuiaClientDescriptor AddDevice(
        this TenantOptions tenant, string clientId, Action<HuiaClientDescriptor>? configure = null) =>
        Add(tenant, clientId, ClientKind.Device, configure);

    /// <summary>Adds a confidential service-to-service client (client credentials grant).</summary>
    /// <param name="tenant">The tenant to add the client to.</param>
    /// <param name="clientId">The client id.</param>
    /// <param name="clientSecret">The client secret.</param>
    /// <param name="configure">Further configuration.</param>
    /// <returns>The new descriptor.</returns>
    public static HuiaClientDescriptor AddMachineToMachineApplication(
        this TenantOptions tenant, string clientId, string clientSecret, Action<HuiaClientDescriptor>? configure = null) =>
        Add(tenant, clientId, ClientKind.MachineToMachine, d => { d.ClientSecret = clientSecret; configure?.Invoke(d); });

    private static HuiaClientDescriptor Add(TenantOptions tenant, string clientId, ClientKind kind, Action<HuiaClientDescriptor>? configure)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        var descriptor = new HuiaClientDescriptor { ClientId = clientId, Kind = kind };
        configure?.Invoke(descriptor);
        tenant.Clients.Add(descriptor);
        return descriptor;
    }
}
