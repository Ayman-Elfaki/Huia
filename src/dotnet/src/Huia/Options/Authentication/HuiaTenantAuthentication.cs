using System.Collections;

namespace Huia.Options;

/// <summary>
/// Composable collection of authentication methods enabled for a tenant.
/// </summary>
public class HuiaTenantAuthentication : IEnumerable<HuiaAuthenticationMethod>, IHuiaOptionsSection
{
    private readonly List<HuiaAuthenticationMethod> _methods = [];

    /// <summary>Read-only view of configured methods.</summary>
    public IReadOnlyList<HuiaAuthenticationMethod> Methods => _methods;

    /// <summary>Default phone country region (ISO 3166-1 alpha-2, e.g. "US").</summary>
    public string? DefaultPhoneCountry { get; set; }

    /// <summary>Whether interactive email/password login is enabled.</summary>
    public bool IsEmailAndPasswordLoginEnabled => EmailAndPassword is { IsEnabled: true };

    /// <summary>Whether passwordless phone SMS OTP login is enabled.</summary>
    public bool IsPhoneLoginEnabled => Phone is { IsEnabled: true };

    /// <summary>Whether passkey (WebAuthn / FIDO2) login is enabled.</summary>
    public bool IsPasskeyLoginEnabled => Passkey is { IsEnabled: true };

    /// <summary>Whether external identity federation is enabled.</summary>
    public bool IsExternalLoginEnabled => External is { IsEnabled: true, Providers.Count: > 0 };

    /// <summary>Configured email/password authentication method, if any.</summary>
    public EmailPasswordAuthenticationMethod? EmailAndPassword => Find<EmailPasswordAuthenticationMethod>();

    /// <summary>Configured phone authentication method, if any.</summary>
    public PhoneAuthenticationMethod? Phone => Find<PhoneAuthenticationMethod>();

    /// <summary>Configured passkey authentication method, if any.</summary>
    public PasskeyAuthenticationMethod? Passkey => Find<PasskeyAuthenticationMethod>();

    /// <summary>Configured external login authentication method, if any.</summary>
    public ExternalLoginAuthenticationMethod? External => Find<ExternalLoginAuthenticationMethod>();

    /// <summary>Sets the default phone country.</summary>
    public HuiaTenantAuthentication SetDefaultPhoneCountry(string country)
    {
        DefaultPhoneCountry = country;
        if (Find<PhoneAuthenticationMethod>() is { DefaultCountry: null } phone)
        {
            phone.DefaultCountry = country;
        }
        return this;
    }

    /// <summary>Adds or replaces an authentication method.</summary>
    public HuiaTenantAuthentication Add(HuiaAuthenticationMethod method)
    {
        ArgumentNullException.ThrowIfNull(method);
        _methods.RemoveAll(m => m.GetType() == method.GetType());
        if (method is PhoneAuthenticationMethod phone && phone.DefaultCountry is null && DefaultPhoneCountry is not null)
        {
            phone.DefaultCountry = DefaultPhoneCountry;
        }
        _methods.Add(method);
        return this;
    }

    /// <summary>Finds a configured authentication method of the given type, or null.</summary>
    public TMethod? Find<TMethod>() where TMethod : HuiaAuthenticationMethod =>
        _methods.OfType<TMethod>().FirstOrDefault();

    /// <summary>Gets a required authentication method of the given type, throwing if not found.</summary>
    public TMethod Require<TMethod>() where TMethod : HuiaAuthenticationMethod =>
        Find<TMethod>() ?? throw new InvalidOperationException($"Authentication method '{typeof(TMethod).Name}' is not enabled for this tenant.");

    /// <summary>Enables and configures the email and password authentication method.</summary>
    public HuiaTenantAuthentication UseEmailAndPasswordLogin(Action<EmailPasswordAuthenticationMethod>? configure = null)
    {
        var method = Find<EmailPasswordAuthenticationMethod>() ?? new EmailPasswordAuthenticationMethod();
        configure?.Invoke(method);
        Add(method);
        return this;
    }

    /// <summary>Enables and configures the phone SMS OTP authentication method.</summary>
    public HuiaTenantAuthentication UsePhoneLogin(Action<PhoneAuthenticationMethod>? configure = null)
    {
        var method = Find<PhoneAuthenticationMethod>() ?? new PhoneAuthenticationMethod();
        configure?.Invoke(method);
        Add(method);
        return this;
    }

    /// <summary>Enables and configures the passkey (WebAuthn / FIDO2) authentication method.</summary>
    public HuiaTenantAuthentication UsePasskeyLogin(Action<PasskeyAuthenticationMethod>? configure = null)
    {
        var method = Find<PasskeyAuthenticationMethod>() ?? new PasskeyAuthenticationMethod();
        configure?.Invoke(method);
        Add(method);
        return this;
    }

    /// <summary>Enables and configures external identity providers.</summary>
    public HuiaTenantAuthentication UseExternalLogin(Action<ExternalLoginAuthenticationMethod> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var method = Find<ExternalLoginAuthenticationMethod>() ?? new ExternalLoginAuthenticationMethod();
        configure(method);
        Add(method);
        return this;
    }

    /// <summary>Turns off anonymous registration and phone auto-provisioning.</summary>
    public HuiaTenantAuthentication DisableRegistration()
    {
        if (Find<EmailPasswordAuthenticationMethod>() is { } email)
        {
            email.AllowSelfServiceRegistration = false;
        }
        if (Find<PhoneAuthenticationMethod>() is { } phone)
        {
            phone.AllowAutoProvisioning = false;
        }
        return this;
    }

    /// <inheritdoc />
    public IEnumerator<HuiaAuthenticationMethod> GetEnumerator() => _methods.GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    void IHuiaOptionsSection.Validate(string path, List<string> errors)
    {
        if (!string.IsNullOrWhiteSpace(DefaultPhoneCountry))
        {
            errors.Require(DefaultPhoneCountry.Length == 2 && DefaultPhoneCountry.All(static c => c is >= 'A' and <= 'Z'),
                HuiaOptionsValidation.Combine(path, nameof(DefaultPhoneCountry)),
                "must be a 2-letter uppercase ISO 3166-1 alpha-2 code (e.g. 'US').");
        }

        var anyEnabled = _methods.Any(m => m.IsEnabled);
        errors.Require(anyEnabled, path, "at least one sign-in method must be enabled (email and password, phone, external, or passkey).");

        foreach (var method in _methods)
        {
            ((IHuiaOptionsSection)method).Validate(HuiaOptionsValidation.Combine(path, method.MethodType), errors);
        }
    }
}
