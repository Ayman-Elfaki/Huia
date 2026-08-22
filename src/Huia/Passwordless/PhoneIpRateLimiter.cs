using Microsoft.Extensions.Caching.Hybrid;

namespace Huia.Passwordless;

/// <summary>
/// Enabled implementation of <see cref="IPhoneIpRateLimiter"/>, registered by <c>AddHuia</c> when
/// <c>PasswordlessFlowOptions.EnableIpRateLimiting</c> was called — partitions <see cref="SlidingWindowOtpRateLimiter"/>
/// by client IP address, per <see cref="PhoneIpRateLimitOptions"/>'s three configured windows.
/// </summary>
internal sealed class PhoneIpRateLimiter : IPhoneIpRateLimiter
{
    private readonly SlidingWindowOtpRateLimiter _inner;

    public PhoneIpRateLimiter(HybridCache cache, PhoneIpRateLimitOptions options,
        TimeProvider? timeProvider = null) =>
        _inner = new SlidingWindowOtpRateLimiter(cache, "huia:phone-ip-rate:", options.RequestsPerMinute,
            options.RequestsPerHour, options.RequestsPerDay, timeProvider);

    public ValueTask<PhoneOtpAcquireResult> TryAcquireAsync(string clientIpAddress,
        CancellationToken cancellationToken = default) =>
        _inner.TryAcquireAsync(clientIpAddress, cancellationToken);
}
