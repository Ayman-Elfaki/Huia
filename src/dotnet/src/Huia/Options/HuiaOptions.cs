namespace Huia.Options;

/// <summary>
/// The root of the Huia configuration tree. Bound from the <c>Huia</c> configuration section and/or
/// configured in code via <see cref="HuiaOptionsBuilder"/>.
/// </summary>
public sealed class HuiaOptions : IHuiaOptionsSection
{
    /// <summary>
    /// The base issuer URL. Per-tenant issuers are formed as <c>{Issuer}/{tenant}</c>. Required.
    /// </summary>
    public Uri? Issuer { get; set; }

    /// <summary>
    /// The externally reachable base URL, used to build absolute links in emails when the current
    /// request context is unavailable. Falls back to <see cref="Issuer"/> then the request origin.
    /// </summary>
    public Uri? PublicUrl { get; set; }

    /// <summary>
    /// When <see langword="true"/>, Huia does not require HTTPS (relaxes the OpenIddict transport guard and
    /// the <c>Secure</c> attribute on cookies). For local development and in-process tests only.
    /// </summary>
    public bool DisableTransportSecurityRequirement { get; set; }

    /// <summary>Root SMTP settings, overridable per tenant.</summary>
    public EmailOptions Email { get; } = new();

    /// <summary>Root SMS settings, overridable per tenant.</summary>
    public SmsOptions Sms { get; } = new();

    /// <summary>Signing-key lifecycle settings (shared across tenants).</summary>
    public KeyManagementOptions Keys { get; } = new();

    /// <summary>Controls how the start-up seeders reconcile the options tree with the database.</summary>
    public SeedingOptions Seeding { get; } = new();

    /// <summary>Controls the OpenIddict.Quartz background pruning of authorizations and tokens.</summary>
    public CleanupOptions Cleanup { get; } = new();

    /// <summary>The configured tenants, keyed by tenant identifier (also the base-path segment).</summary>
    public IDictionary<string, TenantOptions> Tenants { get; } =
        new Dictionary<string, TenantOptions>(StringComparer.Ordinal);

    /// <summary>Adds (or returns the existing) tenant and applies the configuration callback.</summary>
    /// <param name="tenantId">The tenant identifier / base-path segment.</param>
    /// <param name="configure">Configuration for the tenant.</param>
    /// <returns>The tenant options.</returns>
    public TenantOptions AddTenant(string tenantId, Action<TenantOptions> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(configure);

        if (!Tenants.TryGetValue(tenantId, out var tenant))
        {
            tenant = new TenantOptions();
            Tenants[tenantId] = tenant;
        }

        configure(tenant);
        return tenant;
    }

    /// <summary>Runs full-tree validation and throws <see cref="HuiaOptionsException"/> if anything is wrong.</summary>
    public void Validate()
    {
        var errors = new List<string>();
        ((IHuiaOptionsSection)this).Validate(HuiaConstants.ConfigurationSection, errors);
        if (errors.Count > 0)
        {
            throw new HuiaOptionsException(errors);
        }
    }

    void IHuiaOptionsSection.Validate(string path, List<string> errors)
    {
        errors.Require(Issuer is { IsAbsoluteUri: true }, HuiaOptionsValidation.Combine(path, nameof(Issuer)),
            "is required and must be an absolute URL.");

        if (PublicUrl is not null)
        {
            errors.Require(PublicUrl.IsAbsoluteUri, HuiaOptionsValidation.Combine(path, nameof(PublicUrl)),
                "must be an absolute URL.");
        }

        if (!DisableTransportSecurityRequirement && Issuer is { IsAbsoluteUri: true })
        {
            errors.Require(Issuer.Scheme == Uri.UriSchemeHttps, HuiaOptionsValidation.Combine(path, nameof(Issuer)),
                "must use HTTPS unless DisableTransportSecurityRequirement is set.");
        }

        ((IHuiaOptionsSection)Email).Validate(HuiaOptionsValidation.Combine(path, nameof(Email)), errors);
        ((IHuiaOptionsSection)Sms).Validate(HuiaOptionsValidation.Combine(path, nameof(Sms)), errors);
        ((IHuiaOptionsSection)Keys).Validate(HuiaOptionsValidation.Combine(path, nameof(Keys)), errors);
        ((IHuiaOptionsSection)Seeding).Validate(HuiaOptionsValidation.Combine(path, nameof(Seeding)), errors);
        ((IHuiaOptionsSection)Cleanup).Validate(HuiaOptionsValidation.Combine(path, nameof(Cleanup)), errors);

        errors.Require(Tenants.Count > 0, HuiaOptionsValidation.Combine(path, nameof(Tenants)),
            "at least one tenant must be configured.");

        foreach (var (id, tenant) in Tenants)
        {
            errors.Require(IsValidTenantId(id), HuiaOptionsValidation.Combine(path, $"{nameof(Tenants)}:{id}"),
                "tenant identifier must be 1-64 characters of lower-case letters, digits or hyphens.");
            ((IHuiaOptionsSection)tenant).Validate(HuiaOptionsValidation.Combine(path, $"{nameof(Tenants)}:{id}"), errors);
        }
    }

    /// <summary>Whether a string is a well-formed tenant identifier / base-path segment.</summary>
    /// <param name="tenantId">The candidate identifier.</param>
    /// <returns><see langword="true"/> when the identifier is safe to use in a route and a claim.</returns>
    public static bool IsValidTenantId(string? tenantId) =>
        !string.IsNullOrEmpty(tenantId) &&
        tenantId.Length <= 64 &&
        tenantId.All(static c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-') &&
        tenantId[0] != '-' && tenantId[^1] != '-';
}
