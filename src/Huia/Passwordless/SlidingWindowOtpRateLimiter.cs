using Microsoft.Extensions.Caching.Hybrid;

namespace Huia.Passwordless;

/// <summary>
/// The sliding-window-over-a-cached-timestamp-list core shared by <see cref="PhoneOtpRateLimiter"/> (partitioned
/// by phone number) and <see cref="PhoneIpRateLimiter"/> (partitioned by client IP address) — the algorithm is
/// identical for both, only the meaning of the partition key, which <c>Requests*</c> options feed the three
/// window limits, and <paramref name="keyPrefix"/> (via the constructor) differ.
/// </summary>
/// <remarks>
/// Each partition key's own granted-permit timestamps are stored as a single <see cref="HybridCache"/> entry
/// (oldest first, filtered to <see cref="DayWindow"/> — the largest configured window — on every read), used
/// both to decide whether a new request fits under all three windows and, on a denial, to estimate
/// <see cref="PhoneOtpAcquireResult.RetryAfter"/>. <see cref="HybridCache"/>'s L1 tier can hand back a shared
/// reference to the same list instance to concurrent callers in this process, so the list read on each call is
/// never mutated in place — a filtered copy is built instead, and only that copy (plus the new grant, if any) is
/// ever written back via <see cref="HybridCache.SetAsync{T}"/>.
///
/// This read-check-write is not atomic across concurrent requests for the same key: two simultaneous requests
/// can both read the same snapshot and both be granted, overrunning a limit by one permit. That's an accepted,
/// deliberate tradeoff — <see cref="HybridCache"/> has no atomic counter primitive, and this is the cost of
/// making the limiter shareable across a scaled-out deployment (via a distributed <c>IDistributedCache</c>
/// registered as HybridCache's L2) without pulling in a distributed-lock dependency.
/// </remarks>
internal sealed class SlidingWindowOtpRateLimiter
{
    private static readonly TimeSpan MinuteWindow = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan HourWindow = TimeSpan.FromHours(1);
    private static readonly TimeSpan DayWindow = TimeSpan.FromDays(1);

    private readonly HybridCache _cache;
    private readonly string _keyPrefix;
    private readonly int _requestsPerMinute;
    private readonly int _requestsPerHour;
    private readonly int _requestsPerDay;
    private readonly TimeProvider _timeProvider;
    private readonly HybridCacheEntryOptions _entryOptions;

    public SlidingWindowOtpRateLimiter(HybridCache cache, string keyPrefix, int requestsPerMinute,
        int requestsPerHour, int requestsPerDay, TimeProvider? timeProvider = null)
    {
        _cache = cache;
        _keyPrefix = keyPrefix;
        _requestsPerMinute = requestsPerMinute;
        _requestsPerHour = requestsPerHour;
        _requestsPerDay = requestsPerDay;
        _timeProvider = timeProvider ?? TimeProvider.System;

        // Re-set on every write (see SetAsync below), so a key that goes quiet has its entry expire on its own
        // rather than needing manual trimming — self-bounding the same way the old in-memory dictionary was.
        _entryOptions = new HybridCacheEntryOptions { Expiration = DayWindow, LocalCacheExpiration = DayWindow };
    }

    public async ValueTask<PhoneOtpAcquireResult> TryAcquireAsync(string partitionKey,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = _keyPrefix + partitionKey;
        var cached = await _cache.GetOrCreateAsync(cacheKey,
                static _ => ValueTask.FromResult<List<DateTimeOffset>>([]),
                _entryOptions, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var now = _timeProvider.GetUtcNow();

        // Never mutate `cached` in place — HybridCache's L1 tier may have handed the same list instance to
        // another concurrent caller. Everything below works off this fresh filtered copy instead.
        var timestamps = cached.Where(t => now - t < DayWindow).ToList();

        if (IsWithinLimit(timestamps, now, MinuteWindow, _requestsPerMinute) &&
            IsWithinLimit(timestamps, now, HourWindow, _requestsPerHour) &&
            IsWithinLimit(timestamps, now, DayWindow, _requestsPerDay))
        {
            timestamps.Add(now);
            await _cache.SetAsync(cacheKey, timestamps, _entryOptions, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return PhoneOtpAcquireResult.Acquired;
        }

        return PhoneOtpAcquireResult.Denied(EstimateRetryAfter(timestamps, now));
    }

    private static bool IsWithinLimit(List<DateTimeOffset> timestamps, DateTimeOffset now, TimeSpan window,
        int limit) =>
        timestamps.Count(t => now - t < window) < limit;

    private TimeSpan EstimateRetryAfter(List<DateTimeOffset> timestamps, DateTimeOffset now)
    {
        var minuteRetry = RetryAfterForWindow(timestamps, now, MinuteWindow, _requestsPerMinute);
        var hourRetry = RetryAfterForWindow(timestamps, now, HourWindow, _requestsPerHour);
        var dayRetry = RetryAfterForWindow(timestamps, now, DayWindow, _requestsPerDay);

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
