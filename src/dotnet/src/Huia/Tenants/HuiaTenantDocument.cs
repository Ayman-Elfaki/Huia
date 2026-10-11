using Huia.Options;

namespace Huia.Tenants;

/// <summary>
/// The persisted shape of a runtime-defined tenant: an explicit, versioned document rather than a reflection
/// dump of <see cref="HuiaTenant"/>, so the stored format is a deliberate contract. It carries email /
/// password, phone and passkey sign-in, branding, email / SMS overrides, roles, scopes and OAuth
/// applications, plus external-provider login (Google, GitHub, Microsoft, generic OpenID Connect).
/// </summary>
public sealed class HuiaTenantDocument
{
    /// <summary>The document format version, bumped when the shape changes incompatibly.</summary>
    public int Version { get; set; } = CurrentVersion;

    /// <summary>The newest format version this build reads and writes.</summary>
    public const int CurrentVersion = 1;

    /// <summary>The tenant's display name.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Branding.</summary>
    public BrandingDocument Branding { get; set; } = new();

    /// <summary>Per-tenant SMTP override.</summary>
    public EmailDocument? Email { get; set; }

    /// <summary>Per-tenant SMS override.</summary>
    public SmsDocument? Sms { get; set; }

    /// <summary>Email-and-password sign-in, when enabled.</summary>
    public EmailPasswordDocument? EmailPassword { get; set; }

    /// <summary>Passwordless SMS sign-in, when enabled.</summary>
    public PhoneDocument? Phone { get; set; }

    /// <summary>Passkey sign-in, when enabled.</summary>
    public PasskeyDocument? Passkey { get; set; }

    /// <summary>External-provider sign-in, when any provider is configured.</summary>
    public ExternalLoginDocument? ExternalLogin { get; set; }

    /// <summary>Role names.</summary>
    public List<string> Roles { get; set; } = [];

    /// <summary>Scopes.</summary>
    public List<ScopeDocument> Scopes { get; set; } = [];

    /// <summary>OAuth applications.</summary>
    public List<ApplicationDocument> Applications { get; set; } = [];

    /// <summary>Captures <paramref name="tenant"/> as a document.</summary>
    public static HuiaTenantDocument From(HuiaTenant tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        var document = new HuiaTenantDocument
        {
            DisplayName = tenant.DisplayName,
            Branding = new BrandingDocument
            {
                DisplayName = tenant.Branding.DisplayName,
                LogoUrl = tenant.Branding.LogoUrl,
                FaviconUrl = tenant.Branding.FaviconUrl,
                AccentColor = tenant.Branding.AccentColor,
                SupportUrl = tenant.Branding.SupportUrl?.ToString(),
                PrivacyUrl = tenant.Branding.PrivacyUrl?.ToString(),
                TermsUrl = tenant.Branding.TermsUrl?.ToString(),
            },
            Roles = [.. tenant.Roles],
            Scopes =
            [
                .. tenant.Scopes.Select(s => new ScopeDocument
                {
                    Name = s.Name,
                    DisplayName = s.DisplayName,
                    Description = s.Description,
                    Resources = [.. s.Resources],
                }),
            ],
        };

        if (tenant.Email is { } email)
        {
            document.Email = new EmailDocument
            {
                Host = email.Host,
                Port = email.Port,
                UseSsl = email.UseSsl,
                FromAddress = email.FromAddress,
                FromName = email.FromName,
                UserName = email.UserName,
                Password = email.Password,
            };
        }

        if (tenant.Sms is { } sms)
        {
            document.Sms = new SmsDocument
            {
                Provider = sms.Provider,
                ApiKey = sms.ApiKey,
                ApiSecret = sms.ApiSecret,
                SenderId = sms.SenderId,
                LogCodesToLogger = sms.LogCodesToLogger,
                PermitLimit = sms.RateLimit.PermitLimit,
                Window = sms.RateLimit.Window,
                MaxPerNumberPerDay = sms.RateLimit.MaxPerNumberPerDay,
            };
        }

        if (tenant.Authentication.EmailAndPassword is { IsEnabled: true } password)
        {
            document.EmailPassword = new EmailPasswordDocument
            {
                MinimumLength = password.MinimumLength,
                RequireDigit = password.RequireDigit,
                RequireUppercase = password.RequireUppercase,
                RequireLowercase = password.RequireLowercase,
                RequireNonAlphanumeric = password.RequireNonAlphanumeric,
                RequiredUniqueChars = password.RequiredUniqueChars,
                RequireConfirmedEmail = password.RequireConfirmedEmail,
                RequireUniqueEmail = password.RequireUniqueEmail,
                AllowSelfServiceRegistration = password.AllowSelfServiceRegistration,
                MaxFailedAccessAttempts = password.MaxFailedAccessAttempts,
                LockoutDuration = password.LockoutDuration,
            };
        }

        if (tenant.Authentication.Phone is { IsEnabled: true } phone)
        {
            document.Phone = new PhoneDocument
            {
                DefaultCountry = phone.DefaultCountry,
                AllowAutoProvisioning = phone.AllowAutoProvisioning,
                OtpLength = phone.OtpLength,
                OtpLifespan = phone.OtpLifespan,
                MaxVerificationAttempts = phone.MaxVerificationAttempts,
                PendingSignupLifetime = phone.PendingSignupLifetime,
                Captcha = phone.Captcha.ToString(),
                ResendCooldown = phone.ResendCooldown,
                SuccessfulLoginsPerWindow = phone.SuccessfulLoginsPerWindow,
                SuccessfulLoginWindow = phone.SuccessfulLoginWindow,
                SuccessfulLoginsPerDay = phone.SuccessfulLoginsPerDay,
                RequireConfirmedPhoneNumber = phone.RequireConfirmedPhoneNumber,
                MaxFailedAccessAttempts = phone.MaxFailedAccessAttempts,
                LockoutDuration = phone.LockoutDuration,
                AllowedForNewUsers = phone.AllowedForNewUsers,
            };
        }

        if (tenant.Authentication.Passkey is { IsEnabled: true } passkey)
        {
            document.Passkey = new PasskeyDocument
            {
                UserVerification = passkey.UserVerification.ToString(),
                AuthenticatorAttachment = passkey.AuthenticatorAttachment.ToString(),
                RequireResidentKey = passkey.RequireResidentKey,
                RelyingPartyId = passkey.RelyingPartyId,
                AllowedOrigins = [.. passkey.AllowedOrigins],
                AuthenticatorTimeout = passkey.AuthenticatorTimeout,
            };
        }

        if (tenant.Authentication.External is { IsEnabled: true, Providers.Count: > 0 } external)
        {
            document.ExternalLogin = new ExternalLoginDocument
            {
                AccountLinkingEnabled = external.AccountLinkingEnabled,
                AllowedReturnUrlPrefixes = [.. external.AllowedReturnUrlPrefixes],
                Providers = [.. external.Providers.Select(ExternalProviderDocument.From)],
            };
        }

        foreach (var application in tenant.Applications)
        {
            document.Applications.Add(ApplicationDocument.From(application));
        }

        return document;
    }

    /// <summary>
    /// Clears every secret (client secrets, SMTP password, SMS credentials) so the document can be returned to an
    /// administrator without leaking them. Returns this instance.
    /// </summary>
    public HuiaTenantDocument RedactSecrets()
    {
        if (Email is not null)
        {
            Email.Password = null;
        }

        if (Sms is not null)
        {
            Sms.ApiKey = null;
            Sms.ApiSecret = null;
        }

        foreach (var application in Applications)
        {
            application.ClientSecret = null;
        }

        foreach (var provider in ExternalLogin?.Providers ?? [])
        {
            provider.ClientSecret = null;
        }

        return this;
    }

    /// <summary>
    /// Fills in secrets this (typically redacted-then-edited) document leaves unset from <paramref name="existing"/>,
    /// so an update that omits a secret keeps the stored one instead of wiping it. Returns this instance.
    /// </summary>
    public HuiaTenantDocument PreserveSecretsFrom(HuiaTenantDocument existing)
    {
        ArgumentNullException.ThrowIfNull(existing);

        if (Email is not null && existing.Email is not null)
        {
            Email.Password ??= existing.Email.Password;
        }

        if (Sms is not null && existing.Sms is not null)
        {
            Sms.ApiKey ??= existing.Sms.ApiKey;
            Sms.ApiSecret ??= existing.Sms.ApiSecret;
        }

        foreach (var provider in (ExternalLogin?.Providers ?? []).Where(p => string.IsNullOrEmpty(p.ClientSecret)))
        {
            provider.ClientSecret = existing.ExternalLogin?.Providers
                .FirstOrDefault(p => string.Equals(p.Name, provider.Name, StringComparison.OrdinalIgnoreCase))
                ?.ClientSecret;
        }

        foreach (var application in Applications.Where(a => string.IsNullOrEmpty(a.ClientSecret)))
        {
            application.ClientSecret = existing.Applications
                .FirstOrDefault(a => string.Equals(a.ClientId, application.ClientId, StringComparison.Ordinal))
                ?.ClientSecret;
        }

        return this;
    }

    /// <summary>Builds the runtime tenant <paramref name="tenantId"/> from this document.</summary>
    public HuiaTenant ToTenant(string tenantId)
    {
        if (Version > CurrentVersion)
        {
            throw new InvalidOperationException(
                $"Tenant '{tenantId}' was stored with document version {Version}, newer than the supported {CurrentVersion}.");
        }

        var tenant = new HuiaTenant(tenantId) { DisplayName = DisplayName };

        tenant.Branding.DisplayName = Branding.DisplayName;
        tenant.Branding.LogoUrl = Branding.LogoUrl;
        tenant.Branding.FaviconUrl = Branding.FaviconUrl;
        tenant.Branding.AccentColor = Branding.AccentColor;
        tenant.Branding.SupportUrl = ParseUri(Branding.SupportUrl);
        tenant.Branding.PrivacyUrl = ParseUri(Branding.PrivacyUrl);
        tenant.Branding.TermsUrl = ParseUri(Branding.TermsUrl);

        foreach (var role in Roles)
        {
            tenant.Roles.Add(role);
        }

        foreach (var scope in Scopes)
        {
            tenant.AddScope(scope.Name, s =>
            {
                s.DisplayName = scope.DisplayName;
                s.Description = scope.Description;
                foreach (var resource in scope.Resources)
                {
                    s.Resources.Add(resource);
                }
            });
        }

        if (Email is { } email)
        {
            tenant.Email = new EmailOptions
            {
                Host = email.Host,
                Port = email.Port,
                UseSsl = email.UseSsl,
                FromAddress = email.FromAddress,
                FromName = email.FromName,
                UserName = email.UserName,
                Password = email.Password,
            };
        }

        if (Sms is { } sms)
        {
            tenant.Sms = new SmsOptions
            {
                Provider = sms.Provider,
                ApiKey = sms.ApiKey,
                ApiSecret = sms.ApiSecret,
                SenderId = sms.SenderId,
                LogCodesToLogger = sms.LogCodesToLogger,
                RateLimit = new OtpRateLimitOptions
                {
                    PermitLimit = sms.PermitLimit,
                    Window = sms.Window,
                    MaxPerNumberPerDay = sms.MaxPerNumberPerDay,
                },
            };
        }

        if (EmailPassword is { } password)
        {
            tenant.Authentication.UseEmailAndPasswordLogin(p =>
            {
                p.MinimumLength = password.MinimumLength;
                p.RequireDigit = password.RequireDigit;
                p.RequireUppercase = password.RequireUppercase;
                p.RequireLowercase = password.RequireLowercase;
                p.RequireNonAlphanumeric = password.RequireNonAlphanumeric;
                p.RequiredUniqueChars = password.RequiredUniqueChars;
                p.RequireConfirmedEmail = password.RequireConfirmedEmail;
                p.RequireUniqueEmail = password.RequireUniqueEmail;
                p.AllowSelfServiceRegistration = password.AllowSelfServiceRegistration;
                p.MaxFailedAccessAttempts = password.MaxFailedAccessAttempts;
                p.LockoutDuration = password.LockoutDuration;
            });
        }

        if (Phone is { } phone)
        {
            tenant.Authentication.UsePhoneLogin(p =>
            {
                p.DefaultCountry = phone.DefaultCountry;
                p.AllowAutoProvisioning = phone.AllowAutoProvisioning;
                p.OtpLength = phone.OtpLength;
                p.OtpLifespan = phone.OtpLifespan;
                p.MaxVerificationAttempts = phone.MaxVerificationAttempts;
                p.PendingSignupLifetime = phone.PendingSignupLifetime;
                p.Captcha = ParseEnum(phone.Captcha, CaptchaMode.None);
                p.ResendCooldown = phone.ResendCooldown;
                p.SuccessfulLoginsPerWindow = phone.SuccessfulLoginsPerWindow;
                p.SuccessfulLoginWindow = phone.SuccessfulLoginWindow;
                p.SuccessfulLoginsPerDay = phone.SuccessfulLoginsPerDay;
                p.RequireConfirmedPhoneNumber = phone.RequireConfirmedPhoneNumber;
                p.MaxFailedAccessAttempts = phone.MaxFailedAccessAttempts;
                p.LockoutDuration = phone.LockoutDuration;
                p.AllowedForNewUsers = phone.AllowedForNewUsers;
            });
        }

        if (Passkey is { } passkey)
        {
            tenant.Authentication.UsePasskeyLogin(p =>
            {
                p.UserVerification = ParseEnum(passkey.UserVerification, PasskeyUserVerification.Required);
                p.AuthenticatorAttachment = ParseEnum(passkey.AuthenticatorAttachment, PasskeyAuthenticatorAttachment.Any);
                p.RequireResidentKey = passkey.RequireResidentKey;
                p.RelyingPartyId = passkey.RelyingPartyId;
                foreach (var origin in passkey.AllowedOrigins)
                {
                    p.AllowedOrigins.Add(origin);
                }

                p.AuthenticatorTimeout = passkey.AuthenticatorTimeout;
            });
        }

        if (ExternalLogin is { Providers.Count: > 0 } externalLogin)
        {
            tenant.Authentication.UseExternalLogin(external =>
            {
                external.AccountLinkingEnabled = externalLogin.AccountLinkingEnabled;
                foreach (var prefix in externalLogin.AllowedReturnUrlPrefixes)
                {
                    external.AllowedReturnUrlPrefixes.Add(prefix);
                }

                foreach (var provider in externalLogin.Providers)
                {
                    external.Providers.Add(provider.ToProvider());
                }
            });
        }

        foreach (var application in Applications)
        {
            tenant.Applications.Add(application.ToApplication());
        }

        return tenant;
    }

    private static Uri? ParseUri(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : new Uri(value, UriKind.Absolute);

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback)
        where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) ? parsed : fallback;

    /// <summary>Branding section.</summary>
    public sealed class BrandingDocument
    {
        /// <summary>Display name.</summary>
        public string? DisplayName { get; set; }

        /// <summary>Logo URL.</summary>
        public string? LogoUrl { get; set; }

        /// <summary>Favicon URL.</summary>
        public string? FaviconUrl { get; set; }

        /// <summary>Accent colour.</summary>
        public string? AccentColor { get; set; }

        /// <summary>Support page.</summary>
        public string? SupportUrl { get; set; }

        /// <summary>Privacy page.</summary>
        public string? PrivacyUrl { get; set; }

        /// <summary>Terms page.</summary>
        public string? TermsUrl { get; set; }
    }

    /// <summary>SMTP override section.</summary>
    public sealed class EmailDocument
    {
        /// <summary>SMTP host.</summary>
        public string? Host { get; set; }

        /// <summary>SMTP port.</summary>
        public int Port { get; set; } = 587;

        /// <summary>Whether to use TLS.</summary>
        public bool UseSsl { get; set; } = true;

        /// <summary>From address.</summary>
        public string? FromAddress { get; set; }

        /// <summary>From name.</summary>
        public string? FromName { get; set; }

        /// <summary>SMTP user.</summary>
        public string? UserName { get; set; }

        /// <summary>SMTP password.</summary>
        public string? Password { get; set; }
    }

    /// <summary>SMS override section.</summary>
    public sealed class SmsDocument
    {
        /// <summary>Provider name.</summary>
        public string? Provider { get; set; }

        /// <summary>API key.</summary>
        public string? ApiKey { get; set; }

        /// <summary>API secret.</summary>
        public string? ApiSecret { get; set; }

        /// <summary>Sender id.</summary>
        public string? SenderId { get; set; }

        /// <summary>Whether to log codes (development only).</summary>
        public bool LogCodesToLogger { get; set; }

        /// <summary>Permits per window.</summary>
        public int PermitLimit { get; set; } = 3;

        /// <summary>Rate-limit window.</summary>
        public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(15);

        /// <summary>Daily cap per number.</summary>
        public int MaxPerNumberPerDay { get; set; } = 10;
    }

    /// <summary>Email-and-password sign-in settings.</summary>
    public sealed class EmailPasswordDocument
    {
        /// <summary>Minimum password length.</summary>
        public int MinimumLength { get; set; } = 8;

        /// <summary>Require a digit.</summary>
        public bool RequireDigit { get; set; } = true;

        /// <summary>Require an upper-case letter.</summary>
        public bool RequireUppercase { get; set; } = true;

        /// <summary>Require a lower-case letter.</summary>
        public bool RequireLowercase { get; set; } = true;

        /// <summary>Require a non-alphanumeric character.</summary>
        public bool RequireNonAlphanumeric { get; set; }

        /// <summary>Required distinct characters.</summary>
        public int RequiredUniqueChars { get; set; } = 1;

        /// <summary>Require a confirmed email.</summary>
        public bool RequireConfirmedEmail { get; set; }

        /// <summary>Require unique emails.</summary>
        public bool RequireUniqueEmail { get; set; }

        /// <summary>Allow anonymous registration.</summary>
        public bool AllowSelfServiceRegistration { get; set; } = true;

        /// <summary>Failed attempts before lockout.</summary>
        public int MaxFailedAccessAttempts { get; set; } = 5;

        /// <summary>Lockout duration.</summary>
        public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(15);
    }

    /// <summary>Passwordless SMS sign-in settings.</summary>
    public sealed class PhoneDocument
    {
        /// <summary>Default region.</summary>
        public string? DefaultCountry { get; set; }

        /// <summary>Create accounts on first sign-in.</summary>
        public bool AllowAutoProvisioning { get; set; }

        /// <summary>Code length.</summary>
        public int OtpLength { get; set; } = 6;

        /// <summary>Code lifetime.</summary>
        public TimeSpan OtpLifespan { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>Verification attempts per code.</summary>
        public int MaxVerificationAttempts { get; set; } = 5;

        /// <summary>Pending sign-up lifetime.</summary>
        public TimeSpan PendingSignupLifetime { get; set; } = TimeSpan.FromMinutes(30);

        /// <summary>Captcha mode name.</summary>
        public string? Captcha { get; set; }

        /// <summary>Resend cooldown.</summary>
        public TimeSpan ResendCooldown { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>Successful sign-ins per window.</summary>
        public int SuccessfulLoginsPerWindow { get; set; } = 1;

        /// <summary>Successful sign-in window.</summary>
        public TimeSpan SuccessfulLoginWindow { get; set; } = TimeSpan.FromMinutes(2);

        /// <summary>Successful sign-ins per day.</summary>
        public int SuccessfulLoginsPerDay { get; set; } = 5;

        /// <summary>Require a confirmed phone number.</summary>
        public bool RequireConfirmedPhoneNumber { get; set; } = true;

        /// <summary>Failed attempts before lockout.</summary>
        public int MaxFailedAccessAttempts { get; set; } = 5;

        /// <summary>Lockout duration.</summary>
        public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(15);

        /// <summary>Lockout applies to new users.</summary>
        public bool AllowedForNewUsers { get; set; } = true;
    }

    /// <summary>Passkey sign-in settings.</summary>
    public sealed class PasskeyDocument
    {
        /// <summary>User-verification requirement name.</summary>
        public string? UserVerification { get; set; }

        /// <summary>Authenticator attachment name.</summary>
        public string? AuthenticatorAttachment { get; set; }

        /// <summary>Require a discoverable credential.</summary>
        public bool RequireResidentKey { get; set; } = true;

        /// <summary>Relying-party id.</summary>
        public string? RelyingPartyId { get; set; }

        /// <summary>Allowed origins.</summary>
        public List<string> AllowedOrigins { get; set; } = [];

        /// <summary>Ceremony timeout.</summary>
        public TimeSpan AuthenticatorTimeout { get; set; } = TimeSpan.FromMinutes(2);
    }

    /// <summary>External-provider sign-in settings.</summary>
    public sealed class ExternalLoginDocument
    {
        /// <summary>Link an external login to an existing account with the same verified email.</summary>
        public bool AccountLinkingEnabled { get; set; }

        /// <summary>URL prefixes a post-login return URL may point at.</summary>
        public List<string> AllowedReturnUrlPrefixes { get; set; } = [];

        /// <summary>The configured providers.</summary>
        public List<ExternalProviderDocument> Providers { get; set; } = [];
    }

    /// <summary>One external identity provider.</summary>
    public sealed class ExternalProviderDocument
    {
        /// <summary>One of <c>google</c>, <c>github</c>, <c>microsoft</c>, <c>oidc</c>.</summary>
        public string Kind { get; set; } = string.Empty;

        /// <summary>The provider name, unique within the tenant (it appears in the sign-in URL).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>The label shown on the sign-in button.</summary>
        public string? DisplayName { get; set; }

        /// <summary>The OAuth client id registered with the provider.</summary>
        public string ClientId { get; set; } = string.Empty;

        /// <summary>The OAuth client secret registered with the provider.</summary>
        public string? ClientSecret { get; set; }

        /// <summary>The relative redirect URI override (for example <c>signin-google</c>).</summary>
        public string? RedirectUri { get; set; }

        /// <summary>Extra scopes to request.</summary>
        public List<string> Scopes { get; set; } = [];

        /// <summary>The issuer URL (generic OpenID Connect providers only).</summary>
        public string? Authority { get; set; }

        internal static ExternalProviderDocument From(ExternalIdentityProvider provider)
        {
            var document = new ExternalProviderDocument
            {
                Name = provider.Name,
                DisplayName = provider.DisplayName,
                ClientId = provider.ClientId,
                ClientSecret = provider.ClientSecret,
                RedirectUri = provider.RedirectUri?.ToString(),
                Scopes = [.. provider.Scopes],
            };

            switch (provider)
            {
                case GoogleExternalProvider:
                    document.Kind = "google";
                    break;
                case GitHubExternalProvider:
                    document.Kind = "github";
                    break;
                case MicrosoftExternalProvider:
                    document.Kind = "microsoft";
                    break;
                case OpenIdConnectExternalProvider oidc:
                    document.Kind = "oidc";
                    document.Authority = oidc.Authority;
                    break;
                default:
                    throw new NotSupportedException(
                        $"External provider type '{provider.GetType().Name}' cannot be persisted in a tenant document.");
            }

            return document;
        }

        internal ExternalIdentityProvider ToProvider()
        {
            ExternalIdentityProvider provider = Kind switch
            {
                "google" => new GoogleExternalProvider(ClientId, ClientSecret ?? string.Empty),
                "github" => new GitHubExternalProvider(ClientId, ClientSecret ?? string.Empty),
                "microsoft" => new MicrosoftExternalProvider(ClientId, ClientSecret ?? string.Empty),
                "oidc" => new OpenIdConnectExternalProvider(
                    Name, ClientId, ClientSecret ?? string.Empty, Authority ?? string.Empty),
                _ => throw new InvalidOperationException($"Unknown external provider kind '{Kind}'."),
            };

            if (!string.IsNullOrWhiteSpace(Name))
            {
                provider.Name = Name;
            }

            provider.DisplayName = DisplayName ?? provider.DisplayName;
            provider.RedirectUri = string.IsNullOrWhiteSpace(RedirectUri) ? null : new Uri(RedirectUri, UriKind.Relative);
            foreach (var scope in Scopes)
            {
                provider.Scopes.Add(scope);
            }

            return provider;
        }
    }

    /// <summary>A scope.</summary>
    public sealed class ScopeDocument
    {
        /// <summary>Scope name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Display name.</summary>
        public string? DisplayName { get; set; }

        /// <summary>Description.</summary>
        public string? Description { get; set; }

        /// <summary>Resource audiences.</summary>
        public List<string> Resources { get; set; } = [];
    }

    /// <summary>Per-client token lifetimes.</summary>
    public sealed class TokenDocument
    {
        /// <summary>Access token lifetime.</summary>
        public TimeSpan? AccessToken { get; set; }

        /// <summary>Identity token lifetime.</summary>
        public TimeSpan? IdentityToken { get; set; }

        /// <summary>Refresh token lifetime.</summary>
        public TimeSpan? RefreshToken { get; set; }

        /// <summary>Authorization code lifetime.</summary>
        public TimeSpan? AuthorizationCode { get; set; }

        /// <summary>Device code lifetime.</summary>
        public TimeSpan? DeviceCode { get; set; }

        /// <summary>User code lifetime.</summary>
        public TimeSpan? UserCode { get; set; }
    }

    /// <summary>An OAuth application of any kind.</summary>
    public sealed class ApplicationDocument
    {
        /// <summary>One of <c>serverSide</c>, <c>spa</c>, <c>native</c>, <c>m2m</c>, <c>device</c>.</summary>
        public string Kind { get; set; } = string.Empty;

        /// <summary>Client id.</summary>
        public string ClientId { get; set; } = string.Empty;

        /// <summary>Client secret (confidential kinds).</summary>
        public string? ClientSecret { get; set; }

        /// <summary>Display name.</summary>
        public string? DisplayName { get; set; }

        /// <summary>Client home page.</summary>
        public string? ClientUri { get; set; }

        /// <summary>Client logo.</summary>
        public string? LogoUri { get; set; }

        /// <summary>Require consent.</summary>
        public bool RequireConsent { get; set; }

        /// <summary>Allowed scopes.</summary>
        public List<string> Scopes { get; set; } = [];

        /// <summary>Token lifetimes.</summary>
        public TokenDocument Token { get; set; } = new();

        /// <summary>Redirect URIs (interactive kinds).</summary>
        public List<string> RedirectUris { get; set; } = [];

        /// <summary>Post-logout redirect URIs (interactive kinds).</summary>
        public List<string> PostLogoutRedirectUris { get; set; } = [];

        /// <summary>Home URIs (interactive kinds).</summary>
        public List<string> HomeUris { get; set; } = [];

        /// <summary>Require PKCE (server-side and device kinds).</summary>
        public bool RequirePkce { get; set; } = true;

        /// <summary>Require pushed authorization requests (server-side kind).</summary>
        public bool RequiresPushedAuthorizationRequests { get; set; }

        internal static ApplicationDocument From(HuiaApplication application)
        {
            var document = new ApplicationDocument
            {
                ClientId = application.ClientId,
                DisplayName = application.DisplayName,
                ClientUri = application.ClientUri?.ToString(),
                LogoUri = application.LogoUri?.ToString(),
                RequireConsent = application.RequireConsent,
                Scopes = [.. application.Scopes],
                Token = new TokenDocument
                {
                    AccessToken = application.Token.AccessToken,
                    IdentityToken = application.Token.IdentityToken,
                    RefreshToken = application.Token.RefreshToken,
                    AuthorizationCode = application.Token.AuthorizationCode,
                    DeviceCode = application.Token.DeviceCode,
                    UserCode = application.Token.UserCode,
                },
            };

            if (application is InteractiveClientApplication interactive)
            {
                document.RedirectUris = [.. interactive.RedirectUris.Select(u => u.ToString())];
                document.PostLogoutRedirectUris = [.. interactive.PostLogoutRedirectUris.Select(u => u.ToString())];
                document.HomeUris = [.. interactive.HomeUris.Select(u => u.ToString())];
            }

            switch (application)
            {
                case ServerSideWebApplication web:
                    document.Kind = "serverSide";
                    document.ClientSecret = web.ClientSecret;
                    document.RequirePkce = web.RequirePkce;
                    document.RequiresPushedAuthorizationRequests = web.RequiresPushedAuthorizationRequests;
                    break;
                case SinglePageApplication:
                    document.Kind = "spa";
                    break;
                case NativeApplication:
                    document.Kind = "native";
                    break;
                case MachineToMachineApplication m2m:
                    document.Kind = "m2m";
                    document.ClientSecret = m2m.ClientSecret;
                    break;
                case DeviceApplication device:
                    document.Kind = "device";
                    document.ClientSecret = device.ClientSecret;
                    document.RequirePkce = device.RequirePkce;
                    break;
                default:
                    throw new NotSupportedException(
                        $"Application type '{application.GetType().Name}' cannot be persisted in a tenant document.");
            }

            return document;
        }

        internal HuiaApplication ToApplication()
        {
            HuiaApplication application = Kind switch
            {
                "serverSide" => new ServerSideWebApplication(ClientId, ClientSecret ?? string.Empty)
                {
                    RequirePkce = RequirePkce,
                    RequiresPushedAuthorizationRequests = RequiresPushedAuthorizationRequests,
                },
                "spa" => new SinglePageApplication(ClientId),
                "native" => new NativeApplication(ClientId),
                "m2m" => new MachineToMachineApplication(ClientId, ClientSecret ?? string.Empty),
                "device" => new DeviceApplication(ClientId) { ClientSecret = ClientSecret, RequirePkce = RequirePkce },
                _ => throw new InvalidOperationException($"Unknown application kind '{Kind}'."),
            };

            application.DisplayName = DisplayName;
            application.ClientUri = ParseUri(ClientUri);
            application.LogoUri = ParseUri(LogoUri);
            application.RequireConsent = RequireConsent;
            foreach (var scope in Scopes)
            {
                application.Scopes.Add(scope);
            }

            application.Token.AccessToken = Token.AccessToken;
            application.Token.IdentityToken = Token.IdentityToken;
            application.Token.RefreshToken = Token.RefreshToken;
            application.Token.AuthorizationCode = Token.AuthorizationCode;
            application.Token.DeviceCode = Token.DeviceCode;
            application.Token.UserCode = Token.UserCode;

            if (application is InteractiveClientApplication interactive)
            {
                foreach (var uri in RedirectUris)
                {
                    interactive.RedirectUris.Add(new Uri(uri, UriKind.Absolute));
                }

                foreach (var uri in PostLogoutRedirectUris)
                {
                    interactive.PostLogoutRedirectUris.Add(new Uri(uri, UriKind.Absolute));
                }

                foreach (var uri in HomeUris)
                {
                    interactive.HomeUris.Add(new Uri(uri, UriKind.Absolute));
                }
            }

            return application;
        }
    }
}
