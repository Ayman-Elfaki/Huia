using Huia.Options;

namespace Todo.IdentityServer.Tenants;

/// <summary>
/// The Todo business tenant definition.
/// Configured with Emerald branding, email/password login, passkeys, phone SMS login,
/// external identity providers (Google + Partner OIDC), and client applications for Nuxt, Next.js, and Scalar.
/// </summary>
public sealed class TodoTenant : HuiaTenant
{
    private readonly string _issuer;
    private readonly string _todoAppUrl;
    private readonly string _todoNextUrl;
    private readonly string _todoApiUrl;
    private readonly string _externalIssuer;
    private readonly string? _googleClientId;
    private readonly string? _googleClientSecret;

    public TodoTenant(
        string issuer,
        string todoAppUrl,
        string todoNextUrl,
        string todoApiUrl,
        string externalIssuer,
        string? googleClientId,
        string? googleClientSecret) : base("todo")
    {
        _issuer = issuer;
        _todoAppUrl = todoAppUrl;
        _todoNextUrl = todoNextUrl;
        _todoApiUrl = todoApiUrl;
        _externalIssuer = externalIssuer;
        _googleClientId = googleClientId;
        _googleClientSecret = googleClientSecret;

        DisplayName = "Todo";
        Branding.DisplayName = "Todo";
        Branding.LogoUrl = "/brand/huia-logo.svg";
        Branding.FaviconUrl = "/brand/favicon.svg";
        Branding.AccentColor = "#059669";
        Branding.TermsUrl = new Uri($"{_issuer}/legal/terms.html");
        Branding.PrivacyUrl = new Uri($"{_issuer}/legal/privacy.html");
        Branding.SupportUrl = new Uri("https://github.com/Ayman-Elfaki/Huia");
    }

    protected override void ConfigureAuthentication(HuiaTenantAuthentication auth)
    {
        auth.Add(new EmailPasswordAuthenticationMethod
        {
            MinimumLength = 12,
            RequireNonAlphanumeric = true,
            RequireConfirmedEmail = false,
            MaxFailedAccessAttempts = 3,
            LockoutDuration = TimeSpan.FromMinutes(30),
        });

        auth.Add(new PasskeyAuthenticationMethod());

        auth.Add(new PhoneAuthenticationMethod
        {
            DefaultCountry = "SA",
            AllowAutoProvisioning = true,
            SuccessfulLoginsPerWindow = 1,
            SuccessfulLoginWindow = TimeSpan.FromMinutes(2),
            SuccessfulLoginsPerDay = 5,
            MaxFailedAccessAttempts = 5,
        });

        var external = new ExternalLoginAuthenticationMethod();

        external.AddOpenIdConnect(
            "huia", "huia-idp", "huia-idp-secret", $"{_externalIssuer}/partners", p =>
            {
                p.DisplayName = "Partner";
                p.Scopes.Add("profile");
                p.Scopes.Add("email");
            });

        if (!string.IsNullOrWhiteSpace(_googleClientId) && !string.IsNullOrWhiteSpace(_googleClientSecret))
        {
            external.AddGoogle(_googleClientId, _googleClientSecret, g =>
            {
                g.Scopes.Add("email");
                g.Scopes.Add("openid");
                g.Scopes.Add("profile");
            });
        }

        external.EnableAccountsLinking();
        auth.Add(external);
    }

    protected override void ConfigureApplications(HuiaApplicationCollection applications)
    {
        applications.Add(new ServerSideWebApplication("todo-app", "todo-app-secret")
        {
            DisplayName = "Todo",
            ClientUri = new Uri($"{_todoAppUrl}/"),
            LogoUri = new Uri($"{_issuer}/brand/huia-logo.svg"),
            RedirectUris = { new Uri($"{_todoAppUrl}/auth/oidc/callback") },
            PostLogoutRedirectUris = { new Uri($"{_todoAppUrl}/") },
            HomeUris = { new Uri($"{_todoAppUrl}/") },
        });

        applications.Add(new ServerSideWebApplication("todo-next", "todo-next-secret")
        {
            DisplayName = "Todo (Next.js)",
            ClientUri = new Uri($"{_todoNextUrl}/"),
            LogoUri = new Uri($"{_issuer}/brand/huia-logo.svg"),
            RedirectUris = { new Uri($"{_todoNextUrl}/api/auth/callback") },
            PostLogoutRedirectUris = { new Uri($"{_todoNextUrl}/") },
            HomeUris = { new Uri($"{_todoNextUrl}/") },
        });

        applications.Add(new SinglePageApplication("todo-api-docs")
        {
            DisplayName = "Todo API docs (Scalar)",
            ClientUri = new Uri($"{_todoApiUrl}/scalar"),
            RedirectUris = { new Uri($"{_todoApiUrl}/scalar") },
        });
    }

    protected override void ConfigureScopes(IList<HuiaScopeDescriptor> scopes)
    {
        scopes.Add(new HuiaScopeDescriptor
        {
            Name = "reports:read",
            DisplayName = "Read reports",
            Description = "Read-only access to the reporting API.",
            Resources = { "reports-api" },
        });
    }

    protected override void ConfigureRoles(IList<string> roles)
    {
        roles.Add("editor");
        roles.Add("beta-tester");
    }
}
