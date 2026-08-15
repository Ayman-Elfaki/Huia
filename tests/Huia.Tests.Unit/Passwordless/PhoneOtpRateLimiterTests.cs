using Huia.Passwordless;

namespace Huia.Tests.Unit.Passwordless;

/// <summary>
/// Covers permit-count enforcement, per-phone-number isolation, and the <c>RetryAfter</c> estimate a denial
/// carries. Window *expiry* via real elapsed time isn't covered here — the underlying
/// <see cref="System.Threading.RateLimiting.SlidingWindowRateLimiterOptions"/> has no injectable
/// <c>TimeProvider</c> hook to fast-forward, and the real windows are fixed at 1 minute/hour/day (only the
/// permit counts are configurable), so exercising actual enforcement expiry would mean a real 60+ second
/// sleep. <c>FakeTimeProvider</c> only drives this class's own <c>RetryAfter</c> estimate (see
/// <c>PhoneOtpRateLimiter</c>'s own doc comment on why that's tracked independently of the underlying
/// limiters), which doesn't depend on the real limiters' clock at all.
/// </summary>
public class PhoneOtpRateLimiterTests
{
    private static PhoneOtpRateLimiter CreateLimiter(int perMinute = 1, int perHour = 3, int perDay = 10,
        FakeTimeProvider? timeProvider = null) =>
        new(new PhoneOtpRateLimitOptions
        {
            RequestsPerMinute = perMinute,
            RequestsPerHour = perHour,
            RequestsPerDay = perDay,
        }, timeProvider);

    [Fact]
    public async Task TryAcquireAsync_FirstRequestForANumber_Succeeds()
    {
        using var limiter = CreateLimiter();

        var result = await limiter.TryAcquireAsync("+15551234567");
        Assert.True(result.IsAcquired);
        Assert.Equal(TimeSpan.Zero, result.RetryAfter);
    }

    [Fact]
    public async Task TryAcquireAsync_ExceedingPerMinuteLimit_Fails()
    {
        using var limiter = CreateLimiter(perMinute: 1);

        Assert.True((await limiter.TryAcquireAsync("+15551234567")).IsAcquired);
        var denied = await limiter.TryAcquireAsync("+15551234567");
        Assert.False(denied.IsAcquired);
    }

    [Fact]
    public async Task TryAcquireAsync_ExceedingPerMinuteLimit_RetryAfterIsAboutOneMinute()
    {
        var timeProvider = new FakeTimeProvider();
        using var limiter = CreateLimiter(perMinute: 1, timeProvider: timeProvider);

        Assert.True((await limiter.TryAcquireAsync("+15551234567")).IsAcquired);

        timeProvider.Advance(TimeSpan.FromSeconds(10));
        var denied = await limiter.TryAcquireAsync("+15551234567");

        Assert.False(denied.IsAcquired);
        // Granted at t=0, minute window releases at t=60s; 10s have passed, so ~50s remain.
        Assert.InRange(denied.RetryAfter, TimeSpan.FromSeconds(49), TimeSpan.FromSeconds(50));
    }

    [Fact]
    public async Task TryAcquireAsync_ExceedingPerHourLimit_FailsEvenAcrossDifferentMinuteBuckets()
    {
        // A per-minute limit far above what this test issues means only the per-hour limit can be the one
        // rejecting the 4th request.
        using var limiter = CreateLimiter(perMinute: 100, perHour: 3, perDay: 100);
        const string phoneNumber = "+15551234567";

        Assert.True((await limiter.TryAcquireAsync(phoneNumber)).IsAcquired);
        Assert.True((await limiter.TryAcquireAsync(phoneNumber)).IsAcquired);
        Assert.True((await limiter.TryAcquireAsync(phoneNumber)).IsAcquired);
        var denied = await limiter.TryAcquireAsync(phoneNumber);
        Assert.False(denied.IsAcquired);
    }

    [Fact]
    public async Task TryAcquireAsync_ExceedingPerHourLimit_RetryAfterReflectsTheHourWindowNotTheMinuteOne()
    {
        var timeProvider = new FakeTimeProvider();
        using var limiter = CreateLimiter(perMinute: 100, perHour: 1, perDay: 100, timeProvider: timeProvider);
        const string phoneNumber = "+15551234567";

        Assert.True((await limiter.TryAcquireAsync(phoneNumber)).IsAcquired);

        timeProvider.Advance(TimeSpan.FromMinutes(30));
        var denied = await limiter.TryAcquireAsync(phoneNumber);

        Assert.False(denied.IsAcquired);
        // Granted at t=0, hour window releases at t=60m; 30m have passed, so ~30m remain — nowhere near the
        // 1-minute floor a purely minute-window-based estimate would wrongly report.
        Assert.InRange(denied.RetryAfter, TimeSpan.FromMinutes(29), TimeSpan.FromMinutes(30));
    }

    [Fact]
    public async Task TryAcquireAsync_DifferentPhoneNumbers_DoNotShareABucket()
    {
        using var limiter = CreateLimiter(perMinute: 1);

        Assert.True((await limiter.TryAcquireAsync("+15551234567")).IsAcquired);
        Assert.False((await limiter.TryAcquireAsync("+15551234567")).IsAcquired);

        // A different number is unaffected by the first number's exhausted limit.
        Assert.True((await limiter.TryAcquireAsync("+15559998888")).IsAcquired);
    }

    [Fact]
    public async Task TryAcquireAsync_RespectsTheLowestOfAllThreeConfiguredLimits()
    {
        // Per-minute is the tightest of the three here, so it's the one that ends up governing.
        using var limiter = CreateLimiter(perMinute: 2, perHour: 3, perDay: 10);
        const string phoneNumber = "+15551234567";

        Assert.True((await limiter.TryAcquireAsync(phoneNumber)).IsAcquired);
        Assert.True((await limiter.TryAcquireAsync(phoneNumber)).IsAcquired);
        Assert.False((await limiter.TryAcquireAsync(phoneNumber)).IsAcquired);
    }

    /// <summary>Minimal controllable clock — just enough to drive <c>PhoneOtpRateLimiter</c>'s own
    /// <c>RetryAfter</c> estimate deterministically, without pulling in a testing package for one test
    /// file's sake.</summary>
    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }
}
