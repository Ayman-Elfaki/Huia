using System.Text.RegularExpressions;

namespace Huia.Options;

/// <summary>
/// Object-oriented definition of a tenant in Huia. Can be instantiated directly or subclassed
/// to encapsulate branding, authentication methods, applications, scopes, and roles.
/// </summary>
public class HuiaTenant : IHuiaOptionsSection
{
    private static readonly Regex RoleNamePattern = new("^[A-Za-z0-9._:-]{1,256}$", RegexOptions.Compiled);

    /// <summary>The tenant identifier / base-path segment (e.g. "acme").</summary>
    public string Identifier { get; set; } = string.Empty;

    /// <summary>Human-readable tenant name. Defaults to the identifier when unset.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Branding presentation settings.</summary>
    public TenantBrandingOptions Branding { get; } = new();

    /// <summary>Tenant-level SMTP overrides.</summary>
    public EmailOptions? Email { get; set; }

    /// <summary>Tenant-level SMS overrides.</summary>
    public SmsOptions? Sms { get; set; }

    /// <summary>The sign-in methods available for this tenant.</summary>
    public HuiaTenantAuthentication Authentication { get; } = new();

    /// <summary>The OAuth client applications registered for this tenant.</summary>
    public HuiaApplicationCollection Applications { get; } = new();

    /// <summary>Custom OAuth scopes to seed for this tenant.</summary>
    public IList<HuiaScopeDescriptor> Scopes { get; } = [];

    /// <summary>Static roles to seed for this tenant.</summary>
    public IList<string> Roles { get; } = [];

    /// <summary>Default constructor.</summary>
    public HuiaTenant()
    {
    }

    /// <summary>Creates a tenant with an identifier.</summary>
    public HuiaTenant(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        Identifier = identifier;
    }

    /// <summary>Overridable lifecycle hook to configure tenant branding.</summary>
    protected virtual void ConfigureBranding(TenantBrandingOptions branding)
    {
    }

    /// <summary>Overridable lifecycle hook to configure tenant authentication methods.</summary>
    protected virtual void ConfigureAuthentication(HuiaTenantAuthentication authentication)
    {
    }

    /// <summary>Overridable lifecycle hook to configure tenant client applications.</summary>
    protected virtual void ConfigureApplications(HuiaApplicationCollection applications)
    {
    }

    /// <summary>Overridable lifecycle hook to configure tenant scopes.</summary>
    protected virtual void ConfigureScopes(IList<HuiaScopeDescriptor> scopes)
    {
    }

    /// <summary>Overridable lifecycle hook to configure tenant roles.</summary>
    protected virtual void ConfigureRoles(IList<string> roles)
    {
    }

    /// <summary>Runs the configuration lifecycle hooks.</summary>
    public virtual void Initialize()
    {
        ConfigureBranding(Branding);
        ConfigureAuthentication(Authentication);
        ConfigureApplications(Applications);
        ConfigureScopes(Scopes);
        ConfigureRoles(Roles);
    }

    /// <summary>Adds a custom scope descriptor and returns it for further configuration.</summary>
    public HuiaScopeDescriptor AddScope(string name, Action<HuiaScopeDescriptor>? configure = null)
    {
        var descriptor = new HuiaScopeDescriptor { Name = name };
        configure?.Invoke(descriptor);
        Scopes.Add(descriptor);
        return descriptor;
    }

    /// <summary>Declares one or more roles to seed for this tenant.</summary>
    public HuiaTenant AddRoles(params string[] roles)
    {
        foreach (var role in roles)
        {
            Roles.Add(role);
        }
        return this;
    }

    /// <summary>Turns off every anonymous account-creation path for this tenant.</summary>
    public HuiaTenant DisableRegistration()
    {
        Authentication.DisableRegistration();
        return this;
    }

    /// <summary>Validates tenant options.</summary>
    public virtual void Validate(string path, List<string> errors)
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

        ((IHuiaOptionsSection)Applications).Validate(HuiaOptionsValidation.Combine(path, nameof(Applications)), errors);

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

    void IHuiaOptionsSection.Validate(string path, List<string> errors) => Validate(path, errors);
}
