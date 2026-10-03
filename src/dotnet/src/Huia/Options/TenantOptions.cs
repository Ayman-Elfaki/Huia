namespace Huia.Options;

/// <summary>
/// Everything Huia needs to serve one tenant. Inherits from <see cref="HuiaTenant"/>.
/// </summary>
public sealed class TenantOptions : HuiaTenant
{
    private readonly List<HuiaClientDescriptor> _legacyClients = [];

    /// <summary>Default constructor.</summary>
    public TenantOptions()
    {
    }

    /// <summary>Creates a tenant with an identifier.</summary>
    public TenantOptions(string identifier) : base(identifier)
    {
    }

    /// <summary>OAuth clients configured for this tenant.</summary>
    public IList<HuiaClientDescriptor> Clients => _legacyClients;

    /// <summary>Adds a client descriptor to the tenant's applications.</summary>
    public HuiaClientDescriptor AddClient(string clientId, ClientKind kind)
    {
        var descriptor = new HuiaClientDescriptor(clientId, kind);
        _legacyClients.Add(descriptor);
        Applications.Add(descriptor);
        return descriptor;
    }

    /// <inheritdoc />
    public override void Validate(string path, List<string> errors)
    {
        // Sync any clients added directly to Clients into Applications if not already present
        foreach (var client in _legacyClients)
        {
            if (!Applications.Contains(client))
            {
                Applications.Add(client);
            }
        }

        base.Validate(path, errors);
    }
}
