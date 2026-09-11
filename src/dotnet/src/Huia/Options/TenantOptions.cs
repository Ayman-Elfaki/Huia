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

    /// <summary>
    /// Code-defined ("static") roles to seed for this tenant: created at start-up if missing, and
    /// read-only afterwards through the admin API (rename / delete return <c>409</c>).
    /// Only the initial creation is stamped static: a role that already exists under that name is
    /// left alone, whatever its current origin.
    /// </summary>
    public IList<string> Roles { get; } = [];

    /// <summary>Per-flavor extensions (OpenId or Headless) registered for this tenant.</summary>
    public IDictionary<Type, IHuiaOptionsSection> Extensions { get; } = new Dictionary<Type, IHuiaOptionsSection>();

    /// <summary>Gets or adds a typed extension instance.</summary>
    /// <typeparam name="T">The extension type.</typeparam>
    /// <param name="factory">Factory used when the extension is not yet present.</param>
    /// <returns>The extension instance.</returns>
    public T GetOrAddExtension<T>(Func<T> factory) where T : class, IHuiaOptionsSection
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (!Extensions.TryGetValue(typeof(T), out var existing))
        {
            existing = factory();
            Extensions[typeof(T)] = existing;
        }

        return (T)existing;
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

        foreach (var (type, extension) in Extensions)
        {
            var extPath = HuiaOptionsValidation.Combine(path, type.Name);
            extension.Validate(extPath, errors);
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
