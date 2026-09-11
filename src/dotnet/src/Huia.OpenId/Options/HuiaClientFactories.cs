using Huia.OpenId.Options;

namespace Huia.Options;

/// <summary>Factory methods for the supported OAuth client shapes. Each adds a <see cref="HuiaClientDescriptor"/> to a tenant.</summary>
public static class HuiaClientFactories
{
    /// <summary>Adds a confidential server-rendered web application (authorization code + client secret) to OpenId options.</summary>
    public static HuiaClientDescriptor AddServerSideWebApplication(
        this HuiaOpenIdTenantOptions openId, string clientId, string clientSecret, Action<HuiaClientDescriptor>? configure = null) =>
        Add(openId, clientId, ClientKind.ServerSideWebApplication, d => { d.ClientSecret = clientSecret; configure?.Invoke(d); });

    /// <summary>Adds a public browser single-page application (authorization code + PKCE, no secret) to OpenId options.</summary>
    public static HuiaClientDescriptor AddSinglePageApplication(
        this HuiaOpenIdTenantOptions openId, string clientId, Action<HuiaClientDescriptor>? configure = null) =>
        Add(openId, clientId, ClientKind.SinglePageApplication, d => { d.RequirePkce = true; configure?.Invoke(d); });

    /// <summary>Adds a public native / mobile / desktop application (authorization code + PKCE, no secret) to OpenId options.</summary>
    public static HuiaClientDescriptor AddNativeApplication(
        this HuiaOpenIdTenantOptions openId, string clientId, Action<HuiaClientDescriptor>? configure = null) =>
        Add(openId, clientId, ClientKind.NativeApplication, d => { d.RequirePkce = true; configure?.Invoke(d); });

    /// <summary>Adds an input-constrained device client (device authorization grant) to OpenId options.</summary>
    public static HuiaClientDescriptor AddDevice(
        this HuiaOpenIdTenantOptions openId, string clientId, Action<HuiaClientDescriptor>? configure = null) =>
        Add(openId, clientId, ClientKind.Device, configure);

    /// <summary>Adds a confidential service-to-service client (client credentials grant) to OpenId options.</summary>
    public static HuiaClientDescriptor AddMachineToMachineApplication(
        this HuiaOpenIdTenantOptions openId, string clientId, string clientSecret, Action<HuiaClientDescriptor>? configure = null) =>
        Add(openId, clientId, ClientKind.MachineToMachine, d => { d.ClientSecret = clientSecret; configure?.Invoke(d); });

    private static HuiaClientDescriptor Add(HuiaOpenIdTenantOptions openId, string clientId, ClientKind kind, Action<HuiaClientDescriptor>? configure)
    {
        ArgumentNullException.ThrowIfNull(openId);
        var descriptor = new HuiaClientDescriptor { ClientId = clientId, Kind = kind };
        configure?.Invoke(descriptor);
        openId.Clients.Add(descriptor);
        return descriptor;
    }

    /// <summary>Adds OpenID options to a tenant.</summary>
    public static TenantOptions AddHuiaOpenId(this TenantOptions tenant, Action<HuiaOpenIdTenantOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(configure);

        foreach (var extensionType in tenant.Extensions.Keys)
        {
            if (extensionType.Name.Contains("Headless", StringComparison.OrdinalIgnoreCase))
            {
                throw new HuiaOptionsException(
                    "AddHuiaOpenId() cannot be configured on a tenant that already has Headless options configured. " +
                    "Huia.OpenId and Huia.Headless are mutually exclusive.");
            }
        }

        var openIdOptions = tenant.GetOrAddExtension(() => new HuiaOpenIdTenantOptions());
        configure(openIdOptions);
        return tenant;
    }

    /// <summary>Gets the OpenId tenant options if registered.</summary>
    public static HuiaOpenIdTenantOptions? GetHuiaOpenId(this TenantOptions tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        return tenant.Extensions.TryGetValue(typeof(HuiaOpenIdTenantOptions), out var ext)
            ? (HuiaOpenIdTenantOptions)ext
            : null;
    }

    /// <summary>Adds a client to the tenant's OpenID options.</summary>
    public static TenantOptions AddClient(this TenantOptions tenant, HuiaClientDescriptor client)
        => tenant.AddHuiaOpenId(o => o.AddClient(client));

    /// <summary>Adds a scope to the tenant's OpenID options.</summary>
    public static TenantOptions AddScope(this TenantOptions tenant, string name, Action<HuiaScopeDescriptor>? configure = null)
        => tenant.AddHuiaOpenId(o => o.AddScope(name, configure));

    /// <summary>Adds a confidential server-rendered web application to the tenant.</summary>
    public static HuiaClientDescriptor AddServerSideWebApplication(
        this TenantOptions tenant, string clientId, string clientSecret, Action<HuiaClientDescriptor>? configure = null)
    {
        HuiaClientDescriptor? result = null;
        tenant.AddHuiaOpenId(o => result = o.AddServerSideWebApplication(clientId, clientSecret, configure));
        return result!;
    }

    /// <summary>Adds a public SPA to the tenant.</summary>
    public static HuiaClientDescriptor AddSinglePageApplication(
        this TenantOptions tenant, string clientId, Action<HuiaClientDescriptor>? configure = null)
    {
        HuiaClientDescriptor? result = null;
        tenant.AddHuiaOpenId(o => result = o.AddSinglePageApplication(clientId, configure));
        return result!;
    }

    /// <summary>Adds a native application to the tenant.</summary>
    public static HuiaClientDescriptor AddNativeApplication(
        this TenantOptions tenant, string clientId, Action<HuiaClientDescriptor>? configure = null)
    {
        HuiaClientDescriptor? result = null;
        tenant.AddHuiaOpenId(o => result = o.AddNativeApplication(clientId, configure));
        return result!;
    }

    /// <summary>Adds a device client to the tenant.</summary>
    public static HuiaClientDescriptor AddDevice(
        this TenantOptions tenant, string clientId, Action<HuiaClientDescriptor>? configure = null)
    {
        HuiaClientDescriptor? result = null;
        tenant.AddHuiaOpenId(o => result = o.AddDevice(clientId, configure));
        return result!;
    }

    /// <summary>Adds a machine-to-machine client to the tenant.</summary>
    public static HuiaClientDescriptor AddMachineToMachineApplication(
        this TenantOptions tenant, string clientId, string clientSecret, Action<HuiaClientDescriptor>? configure = null)
    {
        HuiaClientDescriptor? result = null;
        tenant.AddHuiaOpenId(o => result = o.AddMachineToMachineApplication(clientId, clientSecret, configure));
        return result!;
    }
}
