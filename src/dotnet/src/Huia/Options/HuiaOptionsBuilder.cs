namespace Huia.Options;

/// <summary>
/// Fluent surface for configuring <see cref="HuiaOptions"/> in code. Wraps a single options instance so
/// the same builder can be used from <c>AddHuia</c> and from unit tests without a DI container.
/// </summary>
public sealed class HuiaOptionsBuilder
{
    /// <summary>Creates a builder over a fresh options instance.</summary>
    public HuiaOptionsBuilder()
        : this(new HuiaOptions())
    {
    }

    /// <summary>Creates a builder over an existing options instance (for example one bound from configuration).</summary>
    /// <param name="options">The options instance to configure.</param>
    public HuiaOptionsBuilder(HuiaOptions options)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>The options instance being configured.</summary>
    public HuiaOptions Options { get; }

    /// <summary>Sets the base issuer URL.</summary>
    /// <param name="issuer">An absolute URL.</param>
    /// <returns>This builder, for chaining.</returns>
    public HuiaOptionsBuilder UseIssuer(string issuer)
    {
        Options.Issuer = new Uri(issuer, UriKind.Absolute);
        return this;
    }

    /// <summary>Sets the externally reachable base URL used for absolute links in emails.</summary>
    /// <param name="publicUrl">An absolute URL.</param>
    /// <returns>This builder, for chaining.</returns>
    public HuiaOptionsBuilder UsePublicUrl(string publicUrl)
    {
        Options.PublicUrl = new Uri(publicUrl, UriKind.Absolute);
        return this;
    }

    /// <summary>Relaxes the HTTPS requirement. For local development and in-process tests only.</summary>
    /// <returns>This builder, for chaining.</returns>
    public HuiaOptionsBuilder DisableTransportSecurityRequirement()
    {
        Options.DisableTransportSecurityRequirement = true;
        return this;
    }

    /// <summary>Configures the root SMTP settings.</summary>
    /// <param name="configure">The configuration callback.</param>
    /// <returns>This builder, for chaining.</returns>
    public HuiaOptionsBuilder ConfigureEmail(Action<EmailOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(Options.Email);
        return this;
    }

    /// <summary>Configures the root SMS settings.</summary>
    /// <param name="configure">The configuration callback.</param>
    /// <returns>This builder, for chaining.</returns>
    public HuiaOptionsBuilder ConfigureSms(Action<SmsOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(Options.Sms);
        return this;
    }

    /// <summary>Configures the signing-key lifecycle.</summary>
    /// <param name="configure">The configuration callback.</param>
    /// <returns>This builder, for chaining.</returns>
    public HuiaOptionsBuilder ConfigureKeys(Action<KeyManagementOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(Options.Keys);
        return this;
    }

    /// <summary>Configures how the start-up seeders reconcile the options tree with the database.</summary>
    /// <param name="configure">The configuration callback.</param>
    /// <returns>This builder, for chaining.</returns>
    public HuiaOptionsBuilder ConfigureSeeding(Action<SeedingOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(Options.Seeding);
        return this;
    }

    /// <summary>Configures the OpenIddict.Quartz background pruning of authorizations and tokens.</summary>
    /// <param name="configure">The configuration callback.</param>
    /// <returns>This builder, for chaining.</returns>
    public HuiaOptionsBuilder ConfigureCleanup(Action<CleanupOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(Options.Cleanup);
        return this;
    }

    /// <summary>Adds or configures a tenant.</summary>
    /// <param name="tenantId">The tenant identifier / base-path segment.</param>
    /// <param name="configure">The configuration callback.</param>
    /// <returns>This builder, for chaining.</returns>
    public HuiaOptionsBuilder AddTenant(string tenantId, Action<TenantOptions> configure)
    {
        Options.AddTenant(tenantId, configure);
        return this;
    }

    /// <summary>Validates the configured tree.</summary>
    /// <returns>The validated <see cref="HuiaOptions"/>.</returns>
    /// <exception cref="HuiaOptionsException">The configuration is invalid.</exception>
    public HuiaOptions Build()
    {
        Options.Validate();
        return Options;
    }
}
