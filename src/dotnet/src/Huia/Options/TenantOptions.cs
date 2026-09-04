namespace Huia.Options;

/// <summary>
/// Everything Huia needs to serve one tenant. Keyed by the tenant identifier in
/// <see cref="HuiaOptions.Tenants"/>; that key is also the base-path segment (<c>/{tenant}/...</c>) and the
/// value written into principals' <c>tenant</c> claim.
/// </summary>
public sealed class TenantOptions : IHuiaOptionsSection
{
    /// <summary>Human-readable tenant name. Defaults to the tenant key when unset.</summary>
    public string? DisplayName { get; set; }

    /// <summary>The sign-in methods available for this tenant. Each carries its own lockout policy.</summary>
    public HuiaTenantAuthenticationOptions Authentication { get; } = new();

    /// <summary>Presentation settings for the account UI and emails.</summary>
    public TenantBrandingOptions Branding { get; } = new();

    /// <summary>Tenant-level SMTP overrides. Merged over <see cref="HuiaOptions.Email"/>.</summary>
    public EmailOptions? Email { get; set; }

    /// <summary>Tenant-level SMS overrides. Merged over <see cref="HuiaOptions.Sms"/>.</summary>
    public SmsOptions? Sms { get; set; }

    /// <summary>OAuth clients to seed for this tenant.</summary>
    public IList<HuiaClientDescriptor> Clients { get; } = [];

    /// <summary>Custom OAuth scopes to seed for this tenant, beyond the standard OIDC scopes.</summary>
    public IList<HuiaScopeDescriptor> Scopes { get; } = [];

    /// <summary>Adds a client descriptor and returns it for further configuration.</summary>
    /// <param name="clientId">The client id.</param>
    /// <param name="kind">The client shape.</param>
    /// <returns>The newly added descriptor.</returns>
    public HuiaClientDescriptor AddClient(string clientId, ClientKind kind)
    {
        var descriptor = new HuiaClientDescriptor { ClientId = clientId, Kind = kind };
        Clients.Add(descriptor);
        return descriptor;
    }

    /// <summary>Adds a custom scope descriptor and returns it for further configuration.</summary>
    /// <param name="name">The scope name.</param>
    /// <param name="configure">Further configuration (display name, description, resources).</param>
    /// <returns>The newly added descriptor.</returns>
    public HuiaScopeDescriptor AddScope(string name, Action<HuiaScopeDescriptor>? configure = null)
    {
        var descriptor = new HuiaScopeDescriptor { Name = name };
        configure?.Invoke(descriptor);
        Scopes.Add(descriptor);
        return descriptor;
    }

    /// <summary>
    /// Turns off every anonymous account-creation path for this tenant — self-service registration
    /// (the account UI hides the "create an account" link and the register page returns 404) and, when
    /// the phone flow is enabled, phone auto-provisioning. See
    /// <see cref="HuiaTenantAuthenticationOptions.DisableRegistration"/>.
    /// </summary>
    /// <returns>This instance, for chaining.</returns>
    public TenantOptions DisableRegistration()
    {
        Authentication.DisableRegistration();
        return this;
    }

    void IHuiaOptionsSection.Validate(string path, List<string> errors)
    {
        ((IHuiaOptionsSection)Authentication).Validate(HuiaOptionsValidation.Combine(path, nameof(Authentication)), errors);
        ((IHuiaOptionsSection)Branding).Validate(HuiaOptionsValidation.Combine(path, nameof(Branding)), errors);
        if (Email is not null)
        {
            ((IHuiaOptionsSection)Email).Validate(HuiaOptionsValidation.Combine(path, nameof(Email)), errors);
        }

        if (Sms is not null)
        {
            ((IHuiaOptionsSection)Sms).Validate(HuiaOptionsValidation.Combine(path, nameof(Sms)), errors);
        }

        var seenClientIds = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < Clients.Count; i++)
        {
            var clientPath = HuiaOptionsValidation.Combine(path, $"{nameof(Clients)}[{i}]");
            ((IHuiaOptionsSection)Clients[i]).Validate(clientPath, errors);
            errors.Require(seenClientIds.Add(Clients[i].ClientId), clientPath,
                $"client id '{Clients[i].ClientId}' is used more than once in this tenant.");
        }

        var seenScopeNames = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < Scopes.Count; i++)
        {
            var scopePath = HuiaOptionsValidation.Combine(path, $"{nameof(Scopes)}[{i}]");
            ((IHuiaOptionsSection)Scopes[i]).Validate(scopePath, errors);
            errors.Require(seenScopeNames.Add(Scopes[i].Name), scopePath,
                $"scope name '{Scopes[i].Name}' is used more than once in this tenant.");
        }
    }
}
