using Huia.Passwordless;

namespace Huia.Tests.Unit.Passwordless;

/// <summary>
/// <see cref="PhoneIpRateLimiter"/> delegates its entire sliding-window/retry-after algorithm to the same
/// <see cref="SlidingWindowOtpRateLimiter"/> core <see cref="PhoneOtpRateLimiter"/> uses — see
/// <c>PhoneOtpRateLimiterTests</c> for that algorithm's exhaustive coverage (window expiry, retry-after
/// estimation across all three windows, etc.), which applies here unchanged. This only proves the IP-specific
/// wiring: it actually reads <see cref="PhoneIpRateLimitOptions"/>, and partitions by IP address correctly.
/// </summary>
public class PhoneIpRateLimiterTests
{
    private static PhoneIpRateLimiter CreateLimiter(int perMinute = 1, int perHour = 3, int perDay = 10) =>
        new(new PhoneIpRateLimitOptions
        {
            RequestsPerMinute = perMinute,
            RequestsPerHour = perHour,
            RequestsPerDay = perDay,
        });

    [Fact]
    public async Task TryAcquireAsync_FirstRequestForAnIp_Succeeds()
    {
        using var limiter = CreateLimiter();

        var result = await limiter.TryAcquireAsync("203.0.113.1");
        Assert.True(result.IsAcquired);
    }

    [Fact]
    public async Task TryAcquireAsync_ExceedingPerMinuteLimit_Fails()
    {
        using var limiter = CreateLimiter(perMinute: 1);

        Assert.True((await limiter.TryAcquireAsync("203.0.113.1")).IsAcquired);
        var denied = await limiter.TryAcquireAsync("203.0.113.1");
        Assert.False(denied.IsAcquired);
    }

    [Fact]
    public async Task TryAcquireAsync_DifferentIpAddresses_DoNotShareABucket()
    {
        using var limiter = CreateLimiter(perMinute: 1);

        Assert.True((await limiter.TryAcquireAsync("203.0.113.1")).IsAcquired);
        Assert.False((await limiter.TryAcquireAsync("203.0.113.1")).IsAcquired);

        // A different IP is unaffected by the first one's exhausted limit — this is what lets one number
        // being requested from many different addresses still be caught by the phone-number limit, while one
        // address cycling through many different numbers is caught by this one instead.
        Assert.True((await limiter.TryAcquireAsync("203.0.113.2")).IsAcquired);
    }

    [Fact]
    public async Task TryAcquireAsync_UsesConfiguredOptionsNotHardcodedDefaults()
    {
        // PhoneOtpRateLimitOptions' own defaults (1/minute) differ from PhoneIpRateLimitOptions' (5/minute) —
        // constructing with an explicit, distinct limit and confirming it's actually enforced (not silently
        // falling back to either type's defaults) proves the right options object is the one being read.
        using var limiter = CreateLimiter(perMinute: 2);

        Assert.True((await limiter.TryAcquireAsync("203.0.113.1")).IsAcquired);
        Assert.True((await limiter.TryAcquireAsync("203.0.113.1")).IsAcquired);
        Assert.False((await limiter.TryAcquireAsync("203.0.113.1")).IsAcquired);
    }
}
