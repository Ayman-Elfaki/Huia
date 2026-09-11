using Huia.Options;

namespace Huia.OpenId.Options;

/// <summary>
/// OpenID Connect / OAuth2 tenant configuration: OAuth clients, custom scopes, and external login providers.
/// </summary>
public sealed class HuiaOpenIdTenantOptions : IHuiaOptionsSection
{
    /// <summary>Code-seeded OAuth clients.</summary>
    public IList<HuiaClientDescriptor> Clients { get; } = [];

    /// <summary>Code-seeded custom scopes.</summary>
    public IList<HuiaScopeDescriptor> Scopes { get; } = [];

    /// <summary>Configured external identity providers (Google, GitHub, Microsoft, custom OIDC).</summary>
    public ExternalLoginOptions? External { get; set; }

    /// <summary>Whether external login is configured for this tenant.</summary>
    public bool IsExternalLoginEnabled => External is not null;

    /// <summary>Declares an OAuth client.</summary>
    public HuiaOpenIdTenantOptions AddClient(HuiaClientDescriptor client)
    {
        ArgumentNullException.ThrowIfNull(client);
        Clients.Add(client);
        return this;
    }

    /// <summary>Declares a custom scope.</summary>
    public HuiaOpenIdTenantOptions AddScope(HuiaScopeDescriptor scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        Scopes.Add(scope);
        return this;
    }

    /// <summary>Declares a custom scope by name.</summary>
    public HuiaOpenIdTenantOptions AddScope(string name, Action<HuiaScopeDescriptor>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var scope = new HuiaScopeDescriptor { Name = name };
        configure?.Invoke(scope);
        return AddScope(scope);
    }

    /// <summary>Enables external login with upstream identity providers.</summary>
    public HuiaOpenIdTenantOptions UseExternalLogin(Action<ExternalLoginOptions>? configure = null)
    {
        External ??= new ExternalLoginOptions();
        configure?.Invoke(External);
        return this;
    }

    /// <inheritdoc />
    public void Validate(string path, List<string> errors)
    {
        ((IHuiaOptionsSection?)External)?.Validate(HuiaOptionsValidation.Combine(path, "External"), errors);

        for (var i = 0; i < Clients.Count; i++)
        {
            ((IHuiaOptionsSection)Clients[i]).Validate(HuiaOptionsValidation.Combine(path, $"Clients[{i}]"), errors);
        }

        var duplicateClientIds = Clients
            .GroupBy(c => c.ClientId, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);
        foreach (var dup in duplicateClientIds)
        {
            errors.Add($"{path}: client id '{dup}' is declared more than once.");
        }

        for (var i = 0; i < Scopes.Count; i++)
        {
            ((IHuiaOptionsSection)Scopes[i]).Validate(HuiaOptionsValidation.Combine(path, $"Scopes[{i}]"), errors);
        }
    }
}
