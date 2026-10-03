using Huia.Options;

namespace Todo.IdentityServer.Tenants;

/// <summary>
/// Tenant used by end-to-end integration tests.
/// Features password, passkeys, phone login with relaxed throttles,
/// a SPA client, worker M2M client, and the Nuxt playground client.
/// </summary>
public sealed class E2ETenant : HuiaTenant
{
    private readonly string _issuer;
    private readonly string _playgroundUrl;

    public E2ETenant(string issuer, string playgroundUrl) : base("e2e")
    {
        _issuer = issuer;
        _playgroundUrl = playgroundUrl;
    }

    protected override void ConfigureAuthentication(HuiaTenantAuthentication auth)
    {
        auth.Add(new EmailPasswordAuthenticationMethod
        {
            RequireConfirmedEmail = false,
        });

        auth.Add(new PasskeyAuthenticationMethod
        {
            UserVerification = PasskeyUserVerification.Preferred,
        });

        auth.Add(new PhoneAuthenticationMethod
        {
            AllowAutoProvisioning = true,
            SuccessfulLoginsPerWindow = 100,
            SuccessfulLoginsPerDay = 1000,
        });
    }

    protected override void ConfigureApplications(HuiaApplicationCollection applications)
    {
        applications.Add(new SinglePageApplication("e2e-spa")
        {
            RedirectUris = { new Uri($"{_issuer}/e2e/e2e-callback") },
        });

        applications.Add(new MachineToMachineApplication("e2e-worker", "e2e-worker-secret"));

        applications.Add(new ServerSideWebApplication("huia-nuxt-playground", "huia-nuxt-playground-secret")
        {
            DisplayName = "huia-nuxt playground",
            RedirectUris = { new Uri($"{_playgroundUrl}/auth/oidc/callback") },
            PostLogoutRedirectUris = { new Uri($"{_playgroundUrl}/") },
            HomeUris = { new Uri($"{_playgroundUrl}/") },
            Token = new TokenLifetimeOptions
            {
                AccessToken = TimeSpan.FromSeconds(35),
                RefreshToken = TimeSpan.FromMinutes(30),
            },
        });
    }
}

/// <summary>
/// Dedicated tenant for testing the self-service signup and email confirmation flow.
/// </summary>
public sealed class E2ESignupTenant : HuiaTenant
{
    public E2ESignupTenant() : base("e2e-signup")
    {
    }

    protected override void ConfigureAuthentication(HuiaTenantAuthentication auth)
    {
        auth.Add(new EmailPasswordAuthenticationMethod
        {
            RequireConfirmedEmail = true,
        });
    }
}
