using Microsoft.Extensions.DependencyInjection;

namespace Huia.Applications;

/// <summary>
/// Declaratively registers the OAuth 2.0/OIDC client applications Huia should seed into the OpenIddict
/// application store on startup. Reachable via <c>HuiaOptions.Applications</c>.
/// </summary>
public sealed class ApplicationsBuilder(IServiceCollection services)
{
    private readonly List<SinglePageApplicationOptions> _singlePageApplications = [];
    private readonly List<NativeApplicationOptions> _nativeApplications = [];
    private readonly List<ServerSideWebApplicationOptions> _serverSideWebApplicationOptions = [];
    private readonly List<MachineToMachineApplicationOptions> _machineToMachineApplications = [];
    private readonly List<DeviceApplicationOptions> _deviceApplications = [];

    /// <summary>
    /// The same <see cref="IServiceCollection"/> passed to <c>services.AddHuia(...)</c> — exposed so a
    /// single <c>ApplicationsBuilder</c> extension method (like <c>Huia.AdminUI</c>'s <c>AddHuiaAdminUI</c>)
    /// can both declare a client application <em>and</em> register whatever DI services it needs, in one
    /// call, from inside <c>AddHuia(issuer, huia => {...})</c>.
    /// </summary>
    public IServiceCollection Services { get; } = services;

    internal IReadOnlyList<DeviceApplicationOptions> DeviceApplications =>
        _deviceApplications;

    internal IReadOnlyList<NativeApplicationOptions> NativeApplications =>
        _nativeApplications;

    internal IReadOnlyList<SinglePageApplicationOptions> SinglePageApplications =>
        _singlePageApplications;

    internal IReadOnlyList<ServerSideWebApplicationOptions> ServerSideWebApplicationOptions =>
        _serverSideWebApplicationOptions;

    internal IReadOnlyList<MachineToMachineApplicationOptions> MachineToMachineApplications =>
        _machineToMachineApplications;


    /// <summary>
    /// Every distinct scope requested via <c>AllowScopes(...)</c> across all registered applications.
    /// </summary>
    internal IReadOnlyCollection<string> GetAllScopes() =>
        [.. AllApplications().SelectMany(a => a.Scopes).Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// Registers a public, browser-based SPA client using authorization_code + PKCE.
    /// </summary>
    public ApplicationsBuilder AddSinglePageApplication(Action<SinglePageApplicationOptions> configure)
    {
        var options = new SinglePageApplicationOptions();
        configure(options);
        options.Validate("single-page");
        _singlePageApplications.Add(options);
        return this;
    }

    /// <summary>
    /// Registers a public, installed-app client using authorization_code + PKCE.
    /// </summary>
    public ApplicationsBuilder AddNativeApplication(Action<NativeApplicationOptions> configure)
    {
        var options = new NativeApplicationOptions();
        configure(options);
        options.Validate("native");
        _nativeApplications.Add(options);
        return this;
    }

    /// <summary>
    /// Registers a confidential, server-rendered web-app client using authorization_code.
    /// </summary>
    public ApplicationsBuilder AddServerSideWebApplication(Action<ServerSideWebApplicationOptions> configure)
    {
        var options = new ServerSideWebApplicationOptions();
        configure(options);
        options.Validate("web");
        _serverSideWebApplicationOptions.Add(options);
        return this;
    }

    /// <summary>
    /// Registers a confidential, non-interactive client using client_credentials.
    /// </summary>
    public ApplicationsBuilder AddMachine2Machine(Action<MachineToMachineApplicationOptions> configure)
    {
        var options = new MachineToMachineApplicationOptions();
        configure(options);
        options.Validate("machine-to-machine");
        _machineToMachineApplications.Add(options);
        return this;
    }

    /// <summary>
    /// Registers a public, input-constrained-device client using the device authorization flow.
    /// </summary>
    public ApplicationsBuilder AddDevice(Action<DeviceApplicationOptions> configure)
    {
        var options = new DeviceApplicationOptions();
        configure(options);
        options.Validate("device");
        _deviceApplications.Add(options);
        return this;
    }


    private IEnumerable<ClientApplicationOptions> AllApplications() =>
        _singlePageApplications.Cast<ClientApplicationOptions>()
            .Concat(_nativeApplications)
            .Concat(_serverSideWebApplicationOptions)
            .Concat(_machineToMachineApplications)
            .Concat(_deviceApplications);
}