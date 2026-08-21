using Microsoft.Extensions.DependencyInjection;

namespace Huia.Authentication;

/// <summary>
/// Configures external (third-party) sign-in providers inside
/// <c>huia.Authentication.UseExternalAuthenticationFlow(ext => {...})</c>. Backed by OpenIddict's own client
/// stack (<c>OpenIddict.Client</c>/<c>OpenIddict.Client.WebIntegration</c>), not ASP.NET Core Identity's
/// generic remote-authentication handlers.
/// </summary>
public sealed class ExternalAuthenticationFlowBuilder(OpenIddictClientBuilder client)
{
    /// <summary>
    /// Registers a named, pre-configured third-party provider — e.g. <c>ext.WebProviders.AddGoogle(google =>
    /// google.SetClientId(...).SetClientSecret(...).SetRedirectUri("callback/login/google"))</c>. Covers
    /// Google, Microsoft, GitHub, and 100+ other services OpenIddict ships settings for out of the box; each
    /// provider needs its own <c>SetRedirectUri(...)</c> (OpenIddict's redirection endpoint for that
    /// provider — see docs/external-providers.md) instead of the old <c>CallbackPath</c> convention. For a
    /// provider without a named integration (a custom-issuer OIDC provider, e.g. a second Huia instance), use
    /// <see cref="Client"/> instead.
    /// </summary>
    public OpenIddictClientWebIntegrationBuilder WebProviders { get; } = client.UseWebProviders();

    /// <summary>
    /// The raw OpenIddict client builder <c>AddHuia</c> registers the external flow onto — use
    /// <c>ext.Client.AddRegistration(new OpenIddictClientRegistration { Issuer = ..., ClientId = ...,
    /// ClientSecret = ..., ProviderName = "...", RedirectUri = ... })</c> to register a provider that isn't
    /// one of <see cref="WebProviders"/>'s named integrations (any generic OAuth2/OIDC provider, identified by
    /// its own issuer). See docs/external-providers.md.
    /// </summary>
    public OpenIddictClientBuilder Client { get; } = client;

    /// <summary>
    /// Whether an external sign-in whose provider-reported email matches an existing password account may
    /// be linked to it after the user proves ownership by entering that account's password, instead of
    /// always requiring them to sign in first and link from account settings. See
    /// <see cref="EnablePasswordLinking"/>.
    /// </summary>
    internal bool PasswordLinkingEnabled { get; private set; }

    /// <summary>
    /// Opts into letting an external sign-in with an email that collides with an existing password account
    /// link itself to that account, once the user proves ownership by entering its password (routed through
    /// the same lockout tracking as a normal password sign-in) — rather than Huia's default of always
    /// redirecting them to sign in first and link the provider from account settings afterward. Off by
    /// default: proving password ownership is a reasonable bar, but it does mean a compromised or
    /// unverified provider-reported email could otherwise link an attacker's external identity onto a
    /// victim's account purely by knowing their password already grants full access — the same access a
    /// signed-in "link from settings" flow assumes, so this only meaningfully changes convenience, not the
    /// actual security boundary. See docs/external-providers.md.
    /// </summary>
    public void EnablePasswordLinking() => PasswordLinkingEnabled = true;
}
