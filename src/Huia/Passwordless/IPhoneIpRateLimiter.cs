namespace Huia.Passwordless;

/// <summary>
/// Throttles OTP requests per client IP address — checked in <c>PhoneLoginModel.OnPostAsync</c> alongside
/// (not instead of) <see cref="IPhoneOtpRateLimiter"/>, so an enumeration script cycling through many phone
/// numbers from one source is still caught even though each individual number never exceeds its own limit.
/// Registered unconditionally by <c>AddHuia</c> — a no-op implementation
/// (always acquires) unless <c>PasswordlessFlowOptions.EnableIpRateLimiting</c> was called, so
/// <c>PhoneLoginModel</c> never needs to branch on whether the feature is actually turned on. The default
/// enabled implementation, <see cref="PhoneIpRateLimiter"/>, is in-memory; register your own
/// <see cref="IPhoneIpRateLimiter"/> before calling <c>AddHuia</c> for a scaled-out deployment where limits
/// must be shared across instances.
/// </summary>
public interface IPhoneIpRateLimiter
{
    /// <summary>
    /// Returns an acquired result and consumes one permit if <paramref name="clientIpAddress"/> is still
    /// within its configured limits; otherwise returns a denied result (with an estimated retry time) without
    /// side effects. See <see cref="PhoneOtpAcquireResult"/>.
    /// </summary>
    ValueTask<PhoneOtpAcquireResult> TryAcquireAsync(string clientIpAddress,
        CancellationToken cancellationToken = default);
}
