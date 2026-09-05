namespace Huia.Options;

/// <summary>The kinds of OAuth client Huia can seed, each mapping to an OpenIddict application shape.</summary>
public enum ClientKind
{
    /// <summary>Confidential server-rendered web application (authorization code + client secret).</summary>
    ServerSideWebApplication = 0,

    /// <summary>Public browser single-page application (authorization code + PKCE, no secret).</summary>
    SinglePageApplication = 1,

    /// <summary>Public native / mobile / desktop application (authorization code + PKCE, no secret).</summary>
    NativeApplication = 2,

    /// <summary>Input-constrained device using the device authorization grant.</summary>
    Device = 3,

    /// <summary>Confidential service-to-service client using the client credentials grant.</summary>
    MachineToMachine = 4,
}

/// <summary>When Huia requires a CAPTCHA challenge during the passwordless phone flow.</summary>
public enum CaptchaMode
{
    /// <summary>Never challenge.</summary>
    None = 0,

    /// <summary>Challenge on every code request.</summary>
    Always = 1,

    /// <summary>Challenge only after a prior failed attempt from the same client.</summary>
    AfterFailure = 2,
}

/// <summary>How strongly a passkey ceremony must assert that the user is present and verified.</summary>
public enum PasskeyUserVerification
{
    /// <summary>User verification is requested but a ceremony still succeeds without it.</summary>
    Preferred = 0,

    /// <summary>User verification (PIN, biometric, …) is mandatory; a ceremony without it is rejected.</summary>
    Required = 1,

    /// <summary>User verification is actively discouraged (for example a pure second-factor token).</summary>
    Discouraged = 2,
}

/// <summary>Which class of authenticator a passkey ceremony should prefer.</summary>
public enum PasskeyAuthenticatorAttachment
{
    /// <summary>No preference — platform and roaming authenticators are both offered.</summary>
    Any = 0,

    /// <summary>Prefer the platform authenticator built into the device (Touch ID, Windows Hello, …).</summary>
    Platform = 1,

    /// <summary>Prefer a roaming authenticator (security key, phone) reachable over USB/NFC/BLE.</summary>
    CrossPlatform = 2,
}

/// <summary>The upstream protocol / vendor an external login provider speaks.</summary>
public enum ExternalProviderKind
{
    /// <summary>Generic OpenID Connect provider configured by authority URL.</summary>
    OpenIdConnect = 0,

    /// <summary>Google (via the OpenIddict web-integration provider).</summary>
    Google = 1,

    /// <summary>GitHub (via the OpenIddict web-integration provider).</summary>
    GitHub = 2,

    /// <summary>Microsoft account (via the OpenIddict web-integration provider).</summary>
    MicrosoftAccount = 3,
}
