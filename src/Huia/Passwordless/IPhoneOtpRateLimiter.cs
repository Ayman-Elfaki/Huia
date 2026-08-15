namespace Huia.Passwordless;

/// <summary>
/// Throttles OTP requests per phone number. Checked before any user lookup/creation in
/// <c>PhoneLoginModel.OnPostAsync</c>, so it works identically for a number with no account yet as for a
/// registered one. The default implementation, <see cref="PhoneOtpRateLimiter"/>, is in-memory; register your
/// own <see cref="IPhoneOtpRateLimiter"/> implementation (e.g. Redis-backed) before calling <c>AddHuia</c> for
/// a scaled-out deployment where limits must be shared across instances.
/// </summary>
public interface IPhoneOtpRateLimiter
{
    /// <summary>
    /// Returns an acquired result and consumes one permit if <paramref name="normalizedPhoneNumber"/>
    /// (E.164) is still within its configured 1/minute, 3/hour, and 10/day limits; otherwise returns a
    /// denied result (with an estimated retry time — see <see cref="PhoneOtpAcquireResult.RetryAfter"/>)
    /// without side effects.
    /// </summary>
    ValueTask<PhoneOtpAcquireResult> TryAcquireAsync(string normalizedPhoneNumber,
        CancellationToken cancellationToken = default);
}
