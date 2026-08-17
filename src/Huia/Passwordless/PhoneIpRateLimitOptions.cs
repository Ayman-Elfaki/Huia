namespace Huia.Passwordless;

/// <summary>
/// Configures the per-client-IP OTP request rate limit — an additional, coarser layer on top of
/// <see cref="PhoneOtpRateLimitOptions"/>'s per-phone-number limit, aimed at a script that cycles through many
/// different phone numbers from the same source (enumeration) rather than hammering one number repeatedly.
/// Off by default — see <c>PasswordlessFlowOptions.EnableIpRateLimiting</c> — since a shared or NATed IP
/// (corporate network, mobile carrier CGNAT, VPN exit node) can plausibly represent many genuine users behind
/// one address; the defaults here are deliberately looser than the per-phone-number ones for that reason, and
/// should be tuned to the app's own expected traffic shape.
/// </summary>
public sealed class PhoneIpRateLimitOptions
{
    /// <summary>
    /// Maximum OTP requests a single client IP may make in any rolling minute. Defaults to 5.
    /// </summary>
    public int RequestsPerMinute { get; set; } = 5;

    /// <summary>
    /// Maximum OTP requests a single client IP may make in any rolling hour. Defaults to 20.
    /// </summary>
    public int RequestsPerHour { get; set; } = 20;

    /// <summary>
    /// Maximum OTP requests a single client IP may make in any rolling day. Defaults to 50.
    /// </summary>
    public int RequestsPerDay { get; set; } = 50;
}
