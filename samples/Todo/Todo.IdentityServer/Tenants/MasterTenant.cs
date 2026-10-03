using Huia.Options;

namespace Todo.IdentityServer.Tenants;

/// <summary>
/// The Master administrative tenant definition.
/// Configured with administrator branding, email/password login (registration disabled),
/// confidential Admin Console web app, and Huia.Cli device client.
/// </summary>
public sealed class MasterTenant : HuiaTenant
{
    private readonly string _issuer;
    private readonly string _adminAppUrl;

    public MasterTenant(string issuer, string adminAppUrl) : base("master")
    {
        _issuer = issuer;
        _adminAppUrl = adminAppUrl;

        DisplayName = "Huia Admin";
        Branding.DisplayName = "Huia Admin";
        Branding.LogoUrl = "/brand/huia-logo.svg";
        Branding.FaviconUrl = "/brand/favicon.svg";
        Branding.AccentColor = "#4f46e5";
        Branding.TermsUrl = new Uri($"{_issuer}/legal/terms.html");
        Branding.PrivacyUrl = new Uri($"{_issuer}/legal/privacy.html");
        Branding.SupportUrl = new Uri("https://github.com/Ayman-Elfaki/Huia");
    }

    protected override void ConfigureAuthentication(HuiaTenantAuthentication auth)
    {
        auth.Add(new EmailPasswordAuthenticationMethod
        {
            RequireConfirmedEmail = false,
            AllowSelfServiceRegistration = false,
            MaxFailedAccessAttempts = 10,
        });
    }

    protected override void ConfigureApplications(HuiaApplicationCollection applications)
    {
        applications.Add(new ServerSideWebApplication("todo-admin", "todo-admin-secret")
        {
            DisplayName = "Huia Admin Console",
            ClientUri = new Uri($"{_adminAppUrl}/"),
            LogoUri = new Uri($"{_issuer}/brand/huia-logo.svg"),
            RequiresPushedAuthorizationRequests = true,
            RedirectUris = { new Uri($"{_adminAppUrl}/auth/oidc/callback") },
            PostLogoutRedirectUris = { new Uri($"{_adminAppUrl}/") },
            HomeUris = { new Uri($"{_adminAppUrl}/") },
        });

        applications.Add(new DeviceApplication("huia-cli")
        {
            ClientSecret = "huia-cli-secret",
            Token = new TokenLifetimeOptions
            {
                DeviceCode = TimeSpan.FromMinutes(10),
                UserCode = TimeSpan.FromMinutes(10),
            },
        });
    }
}
