namespace Huia.Identity;

/// <summary>
/// Huia's own identity configuration surface — replaces ASP.NET Core Identity's <c>IdentityOptions</c>.
/// Configured via <see cref="HuiaOptions.Identity"/>.
/// </summary>
public sealed class HuiaIdentityOptions
{
    /// <summary>Lockout policy.</summary>
    public HuiaLockoutOptions Lockout { get; } = new();

    /// <summary>Password strength policy.</summary>
    public HuiaPasswordOptions Password { get; } = new();

    /// <summary>Sign-in policy.</summary>
    public HuiaSignInOptions SignIn { get; } = new();

    /// <summary>Per-purpose token lifetimes/provider names.</summary>
    public HuiaTokenOptions Tokens { get; } = new();

    /// <summary>
    /// How often the sign-in cookie's embedded security-stamp claim is re-checked against
    /// <see cref="HuiaUser.SecurityStamp"/> — the mechanism that invalidates/refreshes an outstanding session
    /// after a password change or forced sign-out. Defaults to 30 minutes, matching ASP.NET Core Identity's
    /// own default.
    /// </summary>
    public TimeSpan SecurityStampValidationInterval { get; set; } = TimeSpan.FromMinutes(30);
}
