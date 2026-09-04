using System.Text.RegularExpressions;

namespace Huia.Options;

/// <summary>
/// Everything Huia needs to serve one tenant. Keyed by the tenant identifier in
/// <see cref="HuiaOptions.Tenants"/>; that key is also the base-path segment (<c>/{tenant}/...</c>) and the
/// value written into principals' <c>tenant</c> claim.
/// </summary>
public sealed class TenantOptions : IHuiaOptionsSection
{
    private static readonly Regex RoleNamePattern = new("^[A-Za-z0-9._:-]{1,256}$", RegexOptions.Compiled);

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

    /// <summary>
    /// Code-defined ("static") roles to seed for this tenant: created at start-up if missing, and — like
    /// <see cref="Clients"/> / <see cref="Scopes"/> — read-only afterwards through the admin API
    /// (rename / delete return <c>409</c>). Only the initial creation is stamped static: a role that
    /// already exists under that name is left alone, whatever its current origin.
    /// </summary>
    public IList<string> Roles { get; } = [];

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

    /// <summary>Declares one or more roles to seed for this tenant.</summary>
    /// <param name="roles">The role names.</param>
    /// <returns>This instance, for chaining.</returns>
    public TenantOptions AddRoles(params string[] roles)
    {
        foreach (var role in roles)
        {
            Roles.Add(role);
        }

        return this;
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

        var seenRoleNames = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < Roles.Count; i++)
        {
            var rolePath = HuiaOptionsValidation.Combine(path, $"{nameof(Roles)}[{i}]");
            errors.Require(!string.IsNullOrWhiteSpace(Roles[i]), rolePath, "must not be empty.");
            if (string.IsNullOrWhiteSpace(Roles[i]))
            {
                continue;
            }

            errors.Require(RoleNamePattern.IsMatch(Roles[i]), rolePath,
                "must be 1-256 characters of letters, digits, '.', '_', ':' or '-'.");
            errors.Require(seenRoleNames.Add(Roles[i]), rolePath,
                $"role '{Roles[i]}' is declared more than once in this tenant.");
        }
    }
}
