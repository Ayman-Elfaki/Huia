namespace Huia.Options;

/// <summary>Factory methods for the supported OAuth client shapes on <see cref="HuiaTenant"/> and <see cref="TenantOptions"/>.</summary>
public static class HuiaClientFactories
{
    /// <summary>Adds a confidential server-rendered web application (authorization code + client secret).</summary>
    public static ServerSideWebApplication AddServerSideWebApplication(
        this HuiaTenant tenant, string clientId, string clientSecret, Action<ServerSideWebApplication>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        var app = new ServerSideWebApplication(clientId, clientSecret);
        configure?.Invoke(app);
        tenant.Applications.Add(app);
        return app;
    }

    /// <summary>Adds a public browser single-page application (authorization code + PKCE, no secret).</summary>
    public static SinglePageApplication AddSinglePageApplication(
        this HuiaTenant tenant, string clientId, Action<SinglePageApplication>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        var app = new SinglePageApplication(clientId);
        configure?.Invoke(app);
        tenant.Applications.Add(app);
        return app;
    }

    /// <summary>Adds a public native / mobile / desktop application (authorization code + PKCE, no secret).</summary>
    public static NativeApplication AddNativeApplication(
        this HuiaTenant tenant, string clientId, Action<NativeApplication>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        var app = new NativeApplication(clientId);
        configure?.Invoke(app);
        tenant.Applications.Add(app);
        return app;
    }

    /// <summary>Adds an input-constrained device client (device authorization grant).</summary>
    public static DeviceApplication AddDevice(
        this HuiaTenant tenant, string clientId, Action<DeviceApplication>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        var app = new DeviceApplication(clientId);
        configure?.Invoke(app);
        tenant.Applications.Add(app);
        return app;
    }

    /// <summary>Adds a confidential service-to-service client (client credentials grant).</summary>
    public static MachineToMachineApplication AddMachineToMachineApplication(
        this HuiaTenant tenant, string clientId, string clientSecret, Action<MachineToMachineApplication>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        var app = new MachineToMachineApplication(clientId, clientSecret);
        configure?.Invoke(app);
        tenant.Applications.Add(app);
        return app;
    }
}
