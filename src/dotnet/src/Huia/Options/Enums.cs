namespace Huia.Options;

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
