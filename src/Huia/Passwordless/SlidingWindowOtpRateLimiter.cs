using System.Collections.Concurrent;
using System.Threading.RateLimiting;

namespace Huia.Passwordless;

/// <summary>
/// The chained-sliding-window-limiter-plus-retry-after-estimate core shared by <see cref="PhoneOtpRateLimiter"/>
/// (partitioned by phone number) and <see cref="PhoneIpRateLimiter"/> (partitioned by client IP address) — the
/// algorithm is identical for both, only the meaning of the partition key and which
/// <c>Requests*</c> options feed the three window limits differ. See <see cref="PhoneOtpRateLimiter"/>'s own
/// former doc comment (now here) for why grant timestamps are tracked independently of the underlying
/// <see cref="System.Threading.RateLimiting"/> limiters.
/// </summary>
/// <remarks>
/// Also tracks each partition key's own granted-permit timestamps, purely to estimate
/// <see cref="PhoneOtpAcquireResult.RetryAfter"/> on a denial: <see cref="System.Threading.RateLimiting"/>'s
/// sliding-window limiters don't reliably populate a rejected lease's retry-after metadata with
/// <c>QueueLimit</c> 0 (a denial is immediate, with no queued request to compute a wait estimate from), so
/// this reconstructs "the oldest permit still counted in whichever window is exhausted ages out" itself, from
/// data already being recorded for exactly this purpose.
/// </remarks>
internal sealed class SlidingWindowOtpRateLimiter : IDisposable
{
    private static readonly TimeSpan MinuteWindow = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan HourWindow = TimeSpan.FromHours(1);
    private static readonly TimeSpan DayWindow = TimeSpan.FromDays(1);

    private readonly PartitionedRateLimiter<string> _chained;
    private readonly int _requestsPerMinute;
    private readonly int _requestsPerHour;
    private readonly int _requestsPerDay;
    private readonly TimeProvider _timeProvider;

    // One entry per partition key that has ever been granted a permit; each list holds that key's own grant
    // timestamps, oldest first, trimmed to DayWindow (the largest configured window — anything older can
    // never count toward any of the three limits again). Self-bounding: a key that goes quiet has its list
    // emptied out on its next lookup, though the now-empty entry itself is only reclaimed the next time this
    // key is looked up while genuinely idle — acceptable since the underlying rate limiter partitions
    // themselves are equally long-lived per distinct key ever seen.
    private readonly ConcurrentDictionary<string, List<DateTimeOffset>> _grants = new(StringComparer.Ordinal);

    public SlidingWindowOtpRateLimiter(int requestsPerMinute, int requestsPerHour, int requestsPerDay,
        TimeProvider? timeProvider = null)
    {
        _requestsPerMinute = requestsPerMinute;
        _requestsPerHour = requestsPerHour;
        _requestsPerDay = requestsPerDay;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _chained = PartitionedRateLimiter.CreateChained(
            BuildLimiter(MinuteWindow, requestsPerMinute),
            BuildLimiter(HourWindow, requestsPerHour),
            BuildLimiter(DayWindow, requestsPerDay));
    }

    public async ValueTask<PhoneOtpAcquireResult> TryAcquireAsync(string partitionKey,
        CancellationToken cancellationToken = default)
    {
        using var lease = await _chained.AcquireAsync(partitionKey, permitCount: 1, cancellationToken)
            .ConfigureAwait(false);

        var now = _timeProvider.GetUtcNow();
        if (lease.IsAcquired)
        {
            RecordGrant(partitionKey, now);
            return PhoneOtpAcquireResult.Acquired;
        }

        return PhoneOtpAcquireResult.Denied(EstimateRetryAfter(partitionKey, now));
    }

    // CreateChained's own Dispose/DisposeAsync disposes every limiter passed into it, so nothing else needs
    // to be tracked or disposed separately here.
    public void Dispose() => _chained.Dispose();

    private static PartitionedRateLimiter<string> BuildLimiter(TimeSpan window, int permitLimit) =>
        PartitionedRateLimiter.Create<string, string>(key => RateLimitPartition.GetSlidingWindowLimiter(key,
            _ => new SlidingWindowRateLimiterOptions
            {
                Window = window,
                SegmentsPerWindow = 4,
                PermitLimit = permitLimit,
                QueueLimit = 0,
                AutoReplenishment = true,
            }));

    private void RecordGrant(string partitionKey, DateTimeOffset now)
    {
        var timestamps = _grants.GetOrAdd(partitionKey, static _ => []);
        lock (timestamps)
        {
            timestamps.Add(now);
            timestamps.RemoveAll(t => now - t > DayWindow);
        }
    }

    private TimeSpan EstimateRetryAfter(string partitionKey, DateTimeOffset now)
    {
        // No grants recorded at all for a key that was just denied is only possible if this instance's own
        // history doesn't cover whatever consumed its permits (e.g. a restart mid-window) — a minute is
        // always a safe, honest floor to suggest.
        if (!_grants.TryGetValue(partitionKey, out var timestamps))
        {
            return MinuteWindow;
        }

        TimeSpan minuteRetry, hourRetry, dayRetry;
        lock (timestamps)
        {
            minuteRetry = RetryAfterForWindow(timestamps, now, MinuteWindow, _requestsPerMinute);
            hourRetry = RetryAfterForWindow(timestamps, now, HourWindow, _requestsPerHour);
            dayRetry = RetryAfterForWindow(timestamps, now, DayWindow, _requestsPerDay);
        }

        var retryAfter = Max(minuteRetry, hourRetry, dayRetry);

        // None of the three windows came back exhausted — a benign race where another request for this key
        // was granted between this one being denied and this estimate running. A minute is still a safe,
        // honest floor rather than claiming the request can be retried immediately.
        return retryAfter > TimeSpan.Zero ? retryAfter : MinuteWindow;
    }

    /// <summary>
    /// <paramref name="timestamps"/> filtered to <paramref name="window"/> is at or over <paramref name="limit"/>
    /// permits when this many of its most recent entries are still inside the window — releases once enough
    /// of the oldest of those age out to drop back under the limit, i.e. once the oldest one still being
    /// counted crosses <paramref name="window"/>. Returns <see cref="TimeSpan.Zero"/> if this window isn't
    /// the (or a) constraint right now.
    /// </summary>
    private static TimeSpan RetryAfterForWindow(List<DateTimeOffset> timestamps, DateTimeOffset now,
        TimeSpan window, int limit)
    {
        var inWindow = timestamps.Where(t => now - t < window).Order().ToList();
        if (inWindow.Count < limit)
        {
            return TimeSpan.Zero;
        }

        var oldestStillCounted = inWindow[^limit];
        var retryAfter = oldestStillCounted + window - now;
        return retryAfter > TimeSpan.Zero ? retryAfter : TimeSpan.Zero;
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b, TimeSpan c) => a > b ? (a > c ? a : c) : (b > c ? b : c);
}
