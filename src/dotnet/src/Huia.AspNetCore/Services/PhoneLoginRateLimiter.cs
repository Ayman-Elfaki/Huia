using System.Collections.Concurrent;
using Huia.Options;

namespace Huia.AspNetCore.Services;

/// <summary>Throttles <em>successful</em> phone sign-ins per number (distinct from code-request throttling).</summary>
public interface IPhoneLoginRateLimiter
{
    /// <summary>
    /// Reports whether a successful phone sign-in for a number is currently within the configured limits
    /// (once per <c>SuccessfulLoginWindow</c>, plus a daily ceiling) <em>without</em> consuming a permit.
    /// Call this before sending a one-time code so the user is told up front.
    /// </summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="phoneNumber">The E.164 number.</param>
    /// <param name="retryAfter">When throttled by the short window, how long until it clears.</param>
    /// <param name="dailyLimitReached">Whether it was the daily ceiling, not the short window, that blocked.</param>
    /// <returns><see langword="true"/> when a sign-in would be allowed.</returns>
    bool CanRecordLogin(string tenantId, string phoneNumber, out TimeSpan? retryAfter, out bool dailyLimitReached);

    /// <summary>
    /// Records a successful phone sign-in for a number and reports whether it was within the limits. This
    /// is the consuming counterpart to <see cref="CanRecordLogin"/>, called once the sign-in completes.
    /// </summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="phoneNumber">The E.164 number.</param>
    /// <param name="retryAfter">When throttled by the short window, how long until it clears.</param>
    /// <param name="dailyLimitReached">Whether it was the daily ceiling, not the short window, that blocked.</param>
    /// <returns><see langword="true"/> when the sign-in was allowed (and recorded).</returns>
    bool TryRecordLogin(string tenantId, string phoneNumber, out TimeSpan? retryAfter, out bool dailyLimitReached);
}

/// <summary>
/// In-memory <see cref="IPhoneLoginRateLimiter"/>: a short fixed window plus a rolling daily ceiling,
/// bucketed by tenant + number and sized from the tenant's <see cref="PhoneOptions"/>. Uses the
/// injected <see cref="TimeProvider"/> so tests can drive the clock, and separates a non-consuming
/// check (<see cref="CanRecordLogin"/>) from the consuming record (<see cref="TryRecordLogin"/>). A
/// distributed deployment swaps this for a store-backed one.
/// </summary>
internal sealed class InMemoryPhoneLoginRateLimiter(HuiaOptions options, TimeProvider timeProvider) : IPhoneLoginRateLimiter
{
    private readonly ConcurrentDictionary<Key, Bucket> _buckets = new();

    public bool CanRecordLogin(string tenantId, string phoneNumber, out TimeSpan? retryAfter, out bool dailyLimitReached)
        => Evaluate(tenantId, phoneNumber, record: false, out retryAfter, out dailyLimitReached);

    public bool TryRecordLogin(string tenantId, string phoneNumber, out TimeSpan? retryAfter, out bool dailyLimitReached)
        => Evaluate(tenantId, phoneNumber, record: true, out retryAfter, out dailyLimitReached);

    private bool Evaluate(string tenantId, string phoneNumber, bool record, out TimeSpan? retryAfter, out bool dailyLimitReached)
    {
        retryAfter = null;
        dailyLimitReached = false;

        var phone = PhoneOptionsFor(tenantId);
        var now = timeProvider.GetUtcNow();
        var bucket = _buckets.GetOrAdd(new Key(tenantId, phoneNumber), _ => new Bucket(now));

        lock (bucket.Gate)
        {
            if (now - bucket.WindowStart >= phone.SuccessfulLoginWindow)
            {
                bucket.WindowStart = now;
                bucket.WindowCount = 0;
            }

            if (now - bucket.DayStart >= TimeSpan.FromDays(1))
            {
                bucket.DayStart = now;
                bucket.DayCount = 0;
            }

            if (bucket.WindowCount >= phone.SuccessfulLoginsPerWindow)
            {
                retryAfter = bucket.WindowStart + phone.SuccessfulLoginWindow - now;
                return false;
            }

            if (bucket.DayCount >= phone.SuccessfulLoginsPerDay)
            {
                dailyLimitReached = true;
                return false;
            }

            if (record)
            {
                bucket.WindowCount++;
                bucket.DayCount++;
            }

            return true;
        }
    }

    private PhoneOptions PhoneOptionsFor(string tenantId) =>
        options.Tenants.TryGetValue(tenantId, out var tenant)
            ? tenant.Authentication.Phone ?? new PhoneOptions()
            : new PhoneOptions();

    private readonly record struct Key(string TenantId, string PhoneNumber);

    private sealed class Bucket(DateTimeOffset start)
    {
        public object Gate { get; } = new();

        public DateTimeOffset WindowStart { get; set; } = start;

        public int WindowCount { get; set; }

        public DateTimeOffset DayStart { get; set; } = start;

        public int DayCount { get; set; }
    }
}
