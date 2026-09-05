using Microsoft.AspNetCore.Identity;

namespace Huia.AspNetCore.Identity;

/// <summary>
/// The authentication flow a request is running. Each flow gets its own <see cref="IdentityOptions"/>
/// instance (see <see cref="HuiaFlowIdentityOptions"/>) and its own <see cref="HuiaUserManager"/> /
/// <see cref="HuiaSignInManager"/> pair via <see cref="IHuiaFlowIdentityFactory"/>, layered on top of
/// the per-tenant policy projection.
/// </summary>
public enum HuiaAuthFlow
{
    /// <summary>
    /// No flow-specific overrides: the tenant policy as projected onto the default
    /// <see cref="IdentityOptions"/>. Used by the self-service <c>/manage</c> API, the admin API,
    /// seeding and one-time-code token storage — anything that is not a sign-in entry point.
    /// </summary>
    Default = 0,

    /// <summary>Interactive email/password sign-in. Gates on a confirmed email when the tenant asks for one.</summary>
    EmailAndPasswordLogin = 1,

    /// <summary>Passwordless SMS one-time-code sign-in. Gates on a confirmed phone number, never an email.</summary>
    PhoneLogin = 2,

    /// <summary>Federated sign-in through an external identity provider. The provider is the confirmed factor.</summary>
    ExternalLogin = 3,

    /// <summary>
    /// Passkey (WebAuthn) sign-in — a discoverable primary assertion, or a step-up second factor after a
    /// password sign-in. Possession of the credential is the confirmed factor.
    /// </summary>
    Passkey = 4,
}
