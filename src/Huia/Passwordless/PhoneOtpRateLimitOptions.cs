namespace Huia.Passwordless;

/// <summary>
/// Configures how often a single phone number may request an OTP, independent of whether it belongs to an
/// existing account — enforced by <see cref="IPhoneOtpRateLimiter"/> before any user lookup/creation, so it
/// throttles abuse (SMS-pumping fraud, enumeration) against numbers with no account at all just as well as
/// registered ones. Reachable via the <c>configureRateLimit</c> parameter of
/// <c>huia.Authentication.UsePasswordlessFlow(...)</c>.
/// </summary>
public sealed class PhoneOtpRateLimitOptions
{
    /// <summary>
    /// Maximum OTP requests a single phone number may make in any rolling minute. Defaults to 1.
    /// </summary>
    public int RequestsPerMinute { get; set; } = 1;

    /// <summary>
    /// Maximum OTP requests a single phone number may make in any rolling hour. Defaults to 3.
    /// </summary>
    public int RequestsPerHour { get; set; } = 3;

    /// <summary>
    /// Maximum OTP requests a single phone number may make in any rolling day. Defaults to 10.
    /// </summary>
    public int RequestsPerDay { get; set; } = 10;
}
