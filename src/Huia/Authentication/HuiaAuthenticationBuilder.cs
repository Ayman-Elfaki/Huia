using Microsoft.Extensions.DependencyInjection;

namespace Huia.Authentication;

/// <summary>
/// Configures which sign-in methods a Huia app accepts. Reachable via <c>HuiaOptions.Authentication</c>
/// inside <c>services.AddHuia(issuer, huia => {...})</c> — call at least one of
/// <see cref="UsePasswordlessFlow"/> and <see cref="UseEmailAndPasswordFlow"/> (any combination of both is
/// fine), plus <see cref="UseExternalAuthenticationFlow"/> if you want it too.
/// <c>AddHuia</c> throws <see cref="InvalidOperationException"/> if neither 1FA method is enabled — which
/// sign-in method(s) an app accepts is a conscious choice, not something to infer from what was left unset.
/// </summary>
public sealed class HuiaAuthenticationBuilder
{
    /// <summary>
    /// Whether <see cref="UsePasswordlessFlow"/> has been called.
    /// </summary>
    internal bool PasswordlessFlowEnabled { get; private set; }

    /// <summary>
    /// Whether <see cref="UseEmailAndPasswordFlow"/> has been called.
    /// </summary>
    internal bool EmailAndPasswordFlowEnabled { get; private set; }

    /// <summary>
    /// Whether <see cref="UseExternalAuthenticationFlow"/> has been called.
    /// </summary>
    internal bool ExternalFlowEnabled { get; private set; }

    /// <summary>
    /// Passwordless phone/OTP sign-in's own configuration — see <see cref="UsePasswordlessFlow"/>. Always
    /// present (with defaults) regardless of whether the flow is actually enabled, so other code can read it
    /// unconditionally; only <see cref="PasswordlessFlowEnabled"/> gates whether it's ever actually applied.
    /// </summary>
    internal PasswordlessFlowOptions Passwordless { get; } = new();

    internal bool ExternalLoginPasswordLinkingEnabled { get; private set; }

    /// <summary>
    /// The consumer's <see cref="UseExternalAuthenticationFlow"/> callback, stashed rather than invoked
    /// immediately: it configures an <see cref="ExternalAuthenticationFlowBuilder"/>, which itself needs the
    /// <see cref="OpenIddictClientBuilder"/> from <c>AddHuia</c>'s own <c>.AddOpenIddict().AddClient(...)</c>
    /// call — that builder doesn't exist yet at the point this class is constructed (inside
    /// <c>HuiaOptions</c>'s constructor, before <c>configure(options)</c> — the consumer's own callback —
    /// even runs). <c>AddHuia</c> calls <see cref="ApplyExternalAuthenticationFlow"/> once the real client
    /// builder is in scope instead.
    /// </summary>
    private Action<ExternalAuthenticationFlowBuilder>? _externalFlowConfiguration;

    /// <summary>
    /// Enables passwordless phone sign-in: a user enters a phone number, receives a one-time code by SMS,
    /// and submits it to sign in — no password involved. Register an <c>ISmsSender&lt;HuiaUser&gt;</c>
    /// before calling <c>AddHuia</c> to actually deliver codes (the default just logs them in Development).
    /// Requires an <c>IHuiaPhoneNumberStore</c> to be registered — <c>.WithEntityFrameworkStores&lt;TContext&gt;()</c>
    /// provides one automatically.
    /// </summary>
    /// <param name="configure">
    /// Customizes this flow's own settings — rate limits (per-phone-number always, per-IP and Turnstile bot
    /// protection both opt-in) and the country picker's default. See <see cref="PasswordlessFlowOptions"/>;
    /// for ASP.NET Core Identity's own shared <c>IdentityOptions</c> (lockout, etc.), see
    /// <c>HuiaOptions.Identity</c> instead.
    /// </param>
    /// <remarks>
    /// See docs/passwordless.md, including its hybrid-auth security considerations if
    /// <see cref="UseEmailAndPasswordFlow"/> is also enabled.
    /// </remarks>
    public HuiaAuthenticationBuilder UsePasswordlessFlow(Action<PasswordlessFlowOptions>? configure = null)
    {
        PasswordlessFlowEnabled = true;
        configure?.Invoke(Passwordless);
        return this;
    }

    /// <summary>
    /// Enables email+password sign-in — Huia's original sign-in method (registration, login, forgot/reset
    /// password, authenticator 2FA). Either this, <see cref="UsePasswordlessFlow"/>, or both must be called —
    /// <c>AddHuia</c> throws otherwise. For ASP.NET Core Identity's own shared <c>IdentityOptions</c>
    /// (lockout, password policy, etc.), see <c>HuiaOptions.Identity</c>.
    /// </summary>
    public HuiaAuthenticationBuilder UseEmailAndPasswordFlow()
    {
        EmailAndPasswordFlowEnabled = true;
        return this;
    }

    /// <summary>
    /// Enables sign-in via external (third-party) identity providers — Google, Microsoft, GitHub, or any
    /// OAuth2/OIDC provider — backed by OpenIddict's own client stack. Replaces the old, removed
    /// <c>HuiaOptions.ExternalLogins</c> property and <c>EnableExternalLoginPasswordLinking()</c> method,
    /// bundled into a single call: <c>huia.Authentication.UseExternalAuthenticationFlow(ext => {
    /// ext.WebProviders.AddGoogle(...); ext.EnablePasswordLinking(); });</c>. See
    /// <see cref="ExternalAuthenticationFlowBuilder"/> and docs/external-providers.md.
    /// </summary>
    public HuiaAuthenticationBuilder UseExternalAuthenticationFlow(Action<ExternalAuthenticationFlowBuilder> configure)
    {
        ExternalFlowEnabled = true;
        _externalFlowConfiguration = configure;
        return this;
    }

    /// <summary>
    /// Invokes the <see cref="UseExternalAuthenticationFlow"/> callback (if any) against the real
    /// <paramref name="client"/> builder — called by <c>AddHuia</c> once it exists, not by
    /// <see cref="UseExternalAuthenticationFlow"/> itself. See <see cref="_externalFlowConfiguration"/>.
    /// </summary>
    internal void ApplyExternalAuthenticationFlow(OpenIddictClientBuilder client)
    {
        if (_externalFlowConfiguration is null)
        {
            return;
        }

        var flow = new ExternalAuthenticationFlowBuilder(client);
        _externalFlowConfiguration(flow);
        ExternalLoginPasswordLinkingEnabled = flow.PasswordLinkingEnabled;
    }
}
