namespace Huia.Options;

/// <summary>
/// SMTP delivery settings. Configured once at the root (<see cref="HuiaOptions.Email"/>) and optionally
/// overridden per tenant (<see cref="TenantOptions.Email"/>); the effective value is produced by
/// <see cref="MergedWith"/>.
/// </summary>
public sealed class EmailOptions : IHuiaOptionsSection
{
    /// <summary>SMTP host name. Required for email to be sent.</summary>
    public string? Host { get; set; }

    /// <summary>SMTP port. Only merged from the root when the tenant did not set <see cref="Host"/>.</summary>
    public int Port { get; set; } = 587;

    /// <summary>Whether to connect with implicit TLS. Only merged from the root when the tenant did not set <see cref="Host"/>.</summary>
    public bool UseSsl { get; set; } = true;

    /// <summary>The envelope-from address.</summary>
    public string? FromAddress { get; set; }

    /// <summary>The display name paired with <see cref="FromAddress"/>.</summary>
    public string? FromName { get; set; }

    /// <summary>SMTP user name, when the server requires authentication.</summary>
    public string? UserName { get; set; }

    /// <summary>SMTP password, when the server requires authentication.</summary>
    public string? Password { get; set; }

    /// <summary>Whether a usable configuration is present (at minimum a host and a from-address).</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress);

    /// <summary>
    /// Produces the effective options for a tenant. Scalar fields fall back to the root value when the
    /// tenant left them unset. <see cref="Port"/> and <see cref="UseSsl"/> are treated as a unit with
    /// <see cref="Host"/>: they are only taken from the root when the tenant did not specify its own host,
    /// otherwise a tenant pointing at a different server would silently inherit the wrong transport.
    /// </summary>
    /// <param name="tenant">The tenant-level overrides, or <see langword="null"/>.</param>
    /// <returns>A new, fully-populated <see cref="EmailOptions"/>.</returns>
    public EmailOptions MergedWith(EmailOptions? tenant)
    {
        if (tenant is null)
        {
            return Clone();
        }

        var tenantSetHost = !string.IsNullOrWhiteSpace(tenant.Host);
        return new EmailOptions
        {
            Host = tenant.Host ?? Host,
            Port = tenantSetHost ? tenant.Port : Port,
            UseSsl = tenantSetHost ? tenant.UseSsl : UseSsl,
            FromAddress = tenant.FromAddress ?? FromAddress,
            FromName = tenant.FromName ?? FromName,
            UserName = tenant.UserName ?? UserName,
            Password = tenant.Password ?? Password,
        };
    }

    private EmailOptions Clone() => new()
    {
        Host = Host,
        Port = Port,
        UseSsl = UseSsl,
        FromAddress = FromAddress,
        FromName = FromName,
        UserName = UserName,
        Password = Password,
    };

    void IHuiaOptionsSection.Validate(string path, List<string> errors)
    {
        errors.Require(Port is > 0 and <= 65535, HuiaOptionsValidation.Combine(path, nameof(Port)),
            "must be between 1 and 65535.");

        if (!string.IsNullOrWhiteSpace(Host))
        {
            errors.Require(!string.IsNullOrWhiteSpace(FromAddress),
                HuiaOptionsValidation.Combine(path, nameof(FromAddress)),
                "is required when a Host is configured.");
        }
    }
}
