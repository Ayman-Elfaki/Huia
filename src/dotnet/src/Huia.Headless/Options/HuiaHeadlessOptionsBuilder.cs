using Huia.Options;

namespace Huia.Headless.Options;

/// <summary>
/// Fluent configuration surface for <c>AddHuiaHeadless</c>. Flat by design: Headless is single-tenant and
/// has no account UI, so unlike <c>Huia.OpenId</c>'s <see cref="HuiaOptionsBuilder"/> there is no
/// <c>AddTenant(...)</c> wrapper, no <c>Branding</c> (nothing in Headless's JSON API ever reads it), and no
/// <c>AddClient</c>/<c>AddScope</c>/<c>AddRoles</c> (OAuth-client/OpenIddict seeding concepts Headless never
/// uses — it issues its own bearer tokens, not OpenIddict ones). Internally this still builds one
/// <see cref="TenantOptions"/> under the hood, so the multi-tenancy seam shared with <c>Huia.OpenId</c>
/// (<see cref="Huia.Multitenancy.IHuiaTenantContext"/>) keeps working unchanged.
/// </summary>
public sealed class HuiaHeadlessOptionsBuilder
{
    /// <summary>
    /// The fixed, internal-only tenant identifier Headless configures itself under. Never surfaced to a
    /// Headless host or client — Headless's wire contract has no tenant segment or header.
    /// </summary>
    internal const string TenantId = "default";

    private readonly HuiaOptions _options = new();
    private readonly TenantOptions _tenant;

    internal HuiaHeadlessOptionsBuilder()
    {
        _tenant = _options.AddTenant(TenantId, static _ => { });
    }

    /// <summary>Sets the base issuer URL.</summary>
    /// <param name="issuer">An absolute URL.</param>
    /// <returns>This builder, for chaining.</returns>
    public HuiaHeadlessOptionsBuilder UseIssuer(string issuer)
    {
        _options.Issuer = new Uri(issuer, UriKind.Absolute);
        return this;
    }

    /// <summary>Sets the externally reachable base URL used for absolute links in emails.</summary>
    /// <param name="publicUrl">An absolute URL.</param>
    /// <returns>This builder, for chaining.</returns>
    public HuiaHeadlessOptionsBuilder UsePublicUrl(string publicUrl)
    {
        _options.PublicUrl = new Uri(publicUrl, UriKind.Absolute);
        return this;
    }

    /// <summary>Relaxes the HTTPS requirement. For local development and in-process tests only.</summary>
    /// <returns>This builder, for chaining.</returns>
    public HuiaHeadlessOptionsBuilder DisableTransportSecurityRequirement()
    {
        _options.DisableTransportSecurityRequirement = true;
        return this;
    }

    /// <summary>Configures the root SMTP settings.</summary>
    /// <param name="configure">The configuration callback.</param>
    /// <returns>This builder, for chaining.</returns>
    public HuiaHeadlessOptionsBuilder ConfigureEmail(Action<EmailOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_options.Email);
        return this;
    }

    /// <summary>Configures the root SMS settings.</summary>
    /// <param name="configure">The configuration callback.</param>
    /// <returns>This builder, for chaining.</returns>
    public HuiaHeadlessOptionsBuilder ConfigureSms(Action<SmsOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_options.Sms);
        return this;
    }

    /// <summary>
    /// Enables and configures the interactive email/password flow. Calling it sets
    /// <see cref="EmailAndPasswordLoginOptions.Enabled"/>.
    /// </summary>
    /// <param name="configure">Optional further configuration for the flow.</param>
    /// <returns>This builder, for chaining.</returns>
    public HuiaHeadlessOptionsBuilder UseEmailAndPasswordLogin(Action<EmailAndPasswordLoginOptions>? configure = null)
    {
        _tenant.Authentication.UseEmailAndPasswordLogin(configure);
        return this;
    }

    /// <summary>Enables the passwordless phone (SMS one-time code) sign-in.</summary>
    /// <param name="configure">Optional configuration for the flow.</param>
    /// <returns>This builder, for chaining.</returns>
    public HuiaHeadlessOptionsBuilder UsePhoneLogin(Action<PhoneOptions>? configure = null)
    {
        _tenant.Authentication.UsePhoneLogin(configure);
        return this;
    }

    /// <summary>Enables external identity providers.</summary>
    /// <param name="configure">Configuration that registers at least one provider.</param>
    /// <returns>This builder, for chaining.</returns>
    public HuiaHeadlessOptionsBuilder UseExternalLogin(Action<ExternalLoginOptions> configure)
    {
        _tenant.Authentication.UseExternalLogin(configure);
        return this;
    }

    /// <summary>
    /// Enables passkey (WebAuthn / FIDO2) sign-in: a discoverable one-tap sign-in and a one-time prompt to
    /// set one up after a first sign-in.
    /// </summary>
    /// <param name="configure">Optional configuration for the flow.</param>
    /// <returns>This builder, for chaining.</returns>
    public HuiaHeadlessOptionsBuilder UsePasskeyLogin(Action<PasskeyOptions>? configure = null)
    {
        _tenant.Authentication.UsePasskeyLogin(configure);
        return this;
    }

    /// <summary>
    /// Turns off every anonymous account-creation path — self-service registration and, when the phone
    /// flow is enabled, phone auto-provisioning. Only an administrator can create accounts afterwards.
    /// </summary>
    /// <returns>This builder, for chaining.</returns>
    public HuiaHeadlessOptionsBuilder DisableRegistration()
    {
        _tenant.DisableRegistration();
        return this;
    }

    /// <summary>Validates the configured tree.</summary>
    /// <returns>The validated <see cref="HuiaOptions"/> and its single <see cref="TenantOptions"/>.</returns>
    /// <exception cref="HuiaOptionsException">The configuration is invalid.</exception>
    internal (HuiaOptions Options, TenantOptions Tenant) Build()
    {
        _options.Validate();
        return (_options, _tenant);
    }
}
