namespace Huia.Identity;

/// <summary>Authentication scheme names Huia registers. Replaces ASP.NET Core Identity's <c>IdentityConstants</c>.</summary>
public static class HuiaAuthenticationDefaults
{
    /// <summary>The cookie scheme a signed-in user's session lives under.</summary>
    public const string ApplicationScheme = "Huia.Application";

    /// <summary>The cookie scheme an in-progress external (third-party) sign-in's result is held under until
    /// <c>ExternalLoginModel</c>/<c>ManageExternalLoginsEndpoints</c> consumes it.</summary>
    public const string ExternalScheme = "Huia.External";

    /// <summary>The cookie scheme identifying which user a pending two-factor sign-in is for.</summary>
    public const string TwoFactorUserIdScheme = "Huia.TwoFactorUserId";

    /// <summary>The cookie scheme recording that a browser/device has already completed 2FA recently enough
    /// to skip it on this sign-in ("remember this machine").</summary>
    public const string TwoFactorRememberMeScheme = "Huia.TwoFactorRememberMe";
}
