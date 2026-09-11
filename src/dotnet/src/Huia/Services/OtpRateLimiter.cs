using System.Threading.RateLimiting;
using Huia.Options;

namespace Huia.Services;

/// <summary>Throttles one-time-code requests per phone number.</summary>
public interface IOtpRateLimiter
{
    /// <summary>Attempts to take one permit for a number. Returns <see langword="false"/> when throttled.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="phoneNumber">The E.164 number.</param>
    /// <returns><see langword="true"/> when a code may be issued.</returns>
    bool TryAcquire(string tenantId, string phoneNumber);
}

/// <summary>
/// <see cref="IOtpRateLimiter"/> built on <c>System.Threading.RateLimiting</c> — a short fixed window
/// (<see cref="OtpRateLimitOptions.PermitLimit"/> per <see cref="OtpRateLimitOptions.Window"/>) and a
/// rolling daily ceiling (<see cref="OtpRateLimitOptions.MaxPerNumberPerDay"/>), both partitioned by
/// tenant + number. A distributed deployment swaps this for a store-backed limiter.
/// </summary>
internal sealed class InMemoryOtpRateLimiter : IOtpRateLimiter, IDisposable
{
    private readonly PartitionedRateLimiter<string> _window;
    private readonly PartitionedRateLimiter<string> _daily;

    public InMemoryOtpRateLimiter(HuiaOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var limits = options.Sms.RateLimit;

        _window = PartitionedRateLimiter.Create<string, string>(key =>
            RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limits.PermitLimit,
                Window = limits.Window,
                QueueLimit = 0,
                AutoReplenishment = true,
            }));

        _daily = PartitionedRateLimiter.Create<string, string>(key =>
            RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limits.MaxPerNumberPerDay,
                Window = TimeSpan.FromDays(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
    }

    public bool TryAcquire(string tenantId, string phoneNumber)
    {
        var key = $"huia:otp-rl:{tenantId}:{phoneNumber}";

        using var windowLease = _window.AttemptAcquire(key);
        if (!windowLease.IsAcquired)
        {
            return false;
        }

        using var dailyLease = _daily.AttemptAcquire(key);
        return dailyLease.IsAcquired;
    }

    public void Dispose()
    {
        _window.Dispose();
        _daily.Dispose();
    }
}
