namespace Huia.Identity;

/// <summary>Lockout policy. Mirrors ASP.NET Core Identity's <c>LockoutOptions</c>.</summary>
public sealed class HuiaLockoutOptions
{
    /// <summary>Whether a brand-new account has lockout enabled by default. Defaults to <see langword="true"/>.</summary>
    public bool AllowedForNewUsers { get; set; } = true;

    /// <summary>Consecutive failed attempts allowed before lockout. Defaults to 5.</summary>
    public int MaxFailedAccessAttempts { get; set; } = 5;

    /// <summary>How long an account stays locked out once triggered. Defaults to 5 minutes.</summary>
    public TimeSpan DefaultLockoutTimeSpan { get; set; } = TimeSpan.FromMinutes(5);
}
