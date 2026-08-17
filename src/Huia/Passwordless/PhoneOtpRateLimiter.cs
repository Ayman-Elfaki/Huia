namespace Huia.Passwordless;

/// <summary>
/// Default <see cref="IPhoneOtpRateLimiter"/> registered by <c>AddHuia</c> when
/// <c>huia.Authentication.UsePasswordlessFlow()</c> is enabled — partitions <see cref="SlidingWindowOtpRateLimiter"/>
/// by the normalized phone number, per <see cref="PhoneOtpRateLimitOptions"/>'s three configured windows.
/// </summary>
internal sealed class PhoneOtpRateLimiter : IPhoneOtpRateLimiter, IDisposable
{
    private readonly SlidingWindowOtpRateLimiter _inner;

    public PhoneOtpRateLimiter(PhoneOtpRateLimitOptions options, TimeProvider? timeProvider = null) =>
        _inner = new SlidingWindowOtpRateLimiter(options.RequestsPerMinute, options.RequestsPerHour,
            options.RequestsPerDay, timeProvider);

    public ValueTask<PhoneOtpAcquireResult> TryAcquireAsync(string normalizedPhoneNumber,
        CancellationToken cancellationToken = default) =>
        _inner.TryAcquireAsync(normalizedPhoneNumber, cancellationToken);

    public void Dispose() => _inner.Dispose();
}
