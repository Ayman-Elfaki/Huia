namespace Huia.Passwordless;

/// <summary>
/// Registered by <c>AddHuia</c> instead of <see cref="PhoneIpRateLimiter"/> when
/// <c>PasswordlessFlowOptions.EnableIpRateLimiting</c> was never called — every IP always acquires, so
/// <c>PhoneLoginModel</c> can call <see cref="IPhoneIpRateLimiter"/> unconditionally without branching on
/// whether the feature is actually turned on.
/// </summary>
internal sealed class NoOpPhoneIpRateLimiter : IPhoneIpRateLimiter
{
    public ValueTask<PhoneOtpAcquireResult> TryAcquireAsync(string clientIpAddress,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(PhoneOtpAcquireResult.Acquired);
}
