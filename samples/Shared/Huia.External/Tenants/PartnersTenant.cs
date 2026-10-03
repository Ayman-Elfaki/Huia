using Huia.Options;

namespace Huia.External.Tenants;

/// <summary>
/// Tenant representing the mock upstream partner identity provider.
/// </summary>
public sealed class PartnersTenant : HuiaTenant
{
    private readonly string _consumerBaseUrl;
    private readonly string _shopConsumerBaseUrl;

    public PartnersTenant(string consumerBaseUrl, string shopConsumerBaseUrl) : base("partners")
    {
        _consumerBaseUrl = consumerBaseUrl;
        _shopConsumerBaseUrl = shopConsumerBaseUrl;

        DisplayName = "Partner Directory";
        Branding.DisplayName = "Partner Directory";
        Branding.LogoUrl = $"{_consumerBaseUrl}/brand/huia-logo.svg";
        Branding.FaviconUrl = $"{_consumerBaseUrl}/brand/favicon.svg";
        Branding.AccentColor = "#d97706";
        Branding.TermsUrl = new Uri($"{_consumerBaseUrl}/legal/terms.html");
        Branding.PrivacyUrl = new Uri($"{_consumerBaseUrl}/legal/privacy.html");
        Branding.SupportUrl = new Uri("https://github.com/Ayman-Elfaki/Huia");
    }

    protected override void ConfigureAuthentication(HuiaTenantAuthentication auth)
    {
        auth.Add(new EmailPasswordAuthenticationMethod
        {
            RequireConfirmedEmail = false,
        });
    }

    protected override void ConfigureApplications(HuiaApplicationCollection applications)
    {
        applications.Add(new ServerSideWebApplication("huia-idp", "huia-idp-secret")
        {
            DisplayName = "Todo (via partner sign-in)",
            ClientUri = new Uri($"{_consumerBaseUrl}/todo/"),
            RedirectUris =
            {
                new Uri($"{_consumerBaseUrl}/todo/signin-huia"),
                new Uri($"{_consumerBaseUrl}/todo/signin-huiapartial"),
            },
            PostLogoutRedirectUris = { new Uri($"{_consumerBaseUrl}/todo/signout-callback-oidc") },
            Scopes = { "email", "profile" },
        });

        applications.Add(new ServerSideWebApplication("shop-api", "shop-api-secret")
        {
            DisplayName = "Shop (via partner sign-in)",
            ClientUri = new Uri($"{_shopConsumerBaseUrl}/"),
            RedirectUris = { new Uri($"{_shopConsumerBaseUrl}/signin-huia") },
            Scopes = { "email", "profile" },
        });
    }
}
