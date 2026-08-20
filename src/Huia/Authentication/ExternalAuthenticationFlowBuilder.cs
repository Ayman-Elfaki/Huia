using Microsoft.AspNetCore.Authentication;

namespace Huia.Authentication;

/// <summary>
/// Configures external (third-party) sign-in providers inside
/// <c>huia.Authentication.UseExternalAuthenticationFlow(ext => {...})</c>.
/// </summary>
public sealed class ExternalAuthenticationFlowBuilder(AuthenticationBuilder providers)
{
    /// <summary>
    /// Registers external (third-party) sign-in providers — e.g. <c>ext.Providers.AddGoogle(google =>
    /// {...})</c>. This is the same <see cref="AuthenticationBuilder"/> <c>AddHuia</c> itself uses for the
    /// <c>Identity.Application</c>/<c>Identity.External</c> cookie schemes, exposed directly rather than
    /// wrapped, so any standard ASP.NET Core remote-authentication handler works unmodified — call whichever
    /// of <c>AddGoogle</c>/<c>AddMicrosoftAccount</c>/<c>AddOpenIdConnect</c>/<c>AddOAuth</c> fits. Only
    /// <c>AddOAuth</c> (a generic OAuth2 handler) ships in the ASP.NET Core shared framework; the others need
    /// their own NuGet package (e.g. <c>dotnet add package Microsoft.AspNetCore.Authentication.Google</c>). A
    /// provider registered here needs no explicit <c>SignInScheme</c> — <c>AddHuia</c> sets
    /// <see cref="AuthenticationOptions.DefaultSignInScheme"/> to
    /// <see cref="Microsoft.AspNetCore.Identity.IdentityConstants.ExternalScheme"/>, the scheme
    /// <c>SignInManager&lt;HuiaUser&gt;.GetExternalLoginInfoAsync()</c> reads from, so every remote handler
    /// lands there by default the same way it would under plain ASP.NET Core Identity. See
    /// docs/external-providers.md.
    /// </summary>
    public AuthenticationBuilder Providers { get; } = providers;

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
