using System.Collections;

namespace Huia.Options;

/// <summary>
/// A collection of <see cref="HuiaApplication"/> instances configured for a tenant.
/// </summary>
public class HuiaApplicationCollection : IList<HuiaApplication>, IHuiaOptionsSection
{
    private readonly List<HuiaApplication> _applications = [];

    /// <inheritdoc />
    public int Count => _applications.Count;

    /// <inheritdoc />
    public bool IsReadOnly => false;

    /// <inheritdoc />
    public HuiaApplication this[int index]
    {
        get => _applications[index];
        set => _applications[index] = value;
    }

    /// <summary>Adds an application to the collection.</summary>
    public void Add(HuiaApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);
        _applications.Add(application);
    }

    /// <summary>Instantiates, adds, and optionally configures an application.</summary>
    public TApp Add<TApp>(Action<TApp>? configure = null) where TApp : HuiaApplication, new()
    {
        var app = new TApp();
        configure?.Invoke(app);
        Add(app);
        return app;
    }

    /// <summary>Adds a server-side web application.</summary>
    public ServerSideWebApplication AddServerSideWeb(string clientId, string clientSecret, Action<ServerSideWebApplication>? configure = null)
    {
        var app = new ServerSideWebApplication(clientId, clientSecret);
        configure?.Invoke(app);
        Add(app);
        return app;
    }

    /// <summary>Adds a single page application.</summary>
    public SinglePageApplication AddSinglePageApp(string clientId, Action<SinglePageApplication>? configure = null)
    {
        var app = new SinglePageApplication(clientId);
        configure?.Invoke(app);
        Add(app);
        return app;
    }

    /// <summary>Adds a native application.</summary>
    public NativeApplication AddNative(string clientId, Action<NativeApplication>? configure = null)
    {
        var app = new NativeApplication(clientId);
        configure?.Invoke(app);
        Add(app);
        return app;
    }

    /// <summary>Adds a machine-to-machine application.</summary>
    public MachineToMachineApplication AddMachineToMachine(string clientId, string clientSecret, Action<MachineToMachineApplication>? configure = null)
    {
        var app = new MachineToMachineApplication(clientId, clientSecret);
        configure?.Invoke(app);
        Add(app);
        return app;
    }

    /// <summary>Adds a device application.</summary>
    public DeviceApplication AddDevice(string clientId, Action<DeviceApplication>? configure = null)
    {
        var app = new DeviceApplication(clientId);
        configure?.Invoke(app);
        Add(app);
        return app;
    }

    /// <summary>Finds an application by client ID.</summary>
    public HuiaApplication? Find(string clientId) =>
        _applications.FirstOrDefault(a => string.Equals(a.ClientId, clientId, StringComparison.Ordinal));

    /// <summary>Finds a strongly-typed application by client ID.</summary>
    public TApp? Find<TApp>(string clientId) where TApp : HuiaApplication =>
        _applications.OfType<TApp>().FirstOrDefault(a => string.Equals(a.ClientId, clientId, StringComparison.Ordinal));

    /// <inheritdoc />
    public int IndexOf(HuiaApplication item) => _applications.IndexOf(item);

    /// <inheritdoc />
    public void Insert(int index, HuiaApplication item) => _applications.Insert(index, item);

    /// <inheritdoc />
    public void RemoveAt(int index) => _applications.RemoveAt(index);

    /// <inheritdoc />
    public void Clear() => _applications.Clear();

    /// <inheritdoc />
    public bool Contains(HuiaApplication item) => _applications.Contains(item);

    /// <inheritdoc />
    public void CopyTo(HuiaApplication[] array, int arrayIndex) => _applications.CopyTo(array, arrayIndex);

    /// <inheritdoc />
    public bool Remove(HuiaApplication item) => _applications.Remove(item);

    /// <inheritdoc />
    public IEnumerator<HuiaApplication> GetEnumerator() => _applications.GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    void IHuiaOptionsSection.Validate(string path, List<string> errors)
    {
        var seenClientIds = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < _applications.Count; i++)
        {
            var app = _applications[i];
            var appPath = HuiaOptionsValidation.Combine(path, $"[{i}]");
            ((IHuiaOptionsSection)app).Validate(appPath, errors);

            if (!string.IsNullOrWhiteSpace(app.ClientId))
            {
                errors.Require(seenClientIds.Add(app.ClientId), appPath,
                    $"client id '{app.ClientId}' is used more than once in this tenant.");
            }
        }
    }
}
