namespace Huia.Passwordless;

/// <summary>
/// The outcome of a single <see cref="IPhoneOtpRateLimiter.TryAcquireAsync"/> call: whether a permit was
/// granted and, if not, how long until another attempt is likely to succeed.
/// </summary>
public readonly struct PhoneOtpAcquireResult
{
    /// <summary>A granted permit — <see cref="RetryAfter"/> is always <see cref="TimeSpan.Zero"/>.</summary>
    public static readonly PhoneOtpAcquireResult Acquired = new(true, TimeSpan.Zero);

    private PhoneOtpAcquireResult(bool isAcquired, TimeSpan retryAfter)
    {
        IsAcquired = isAcquired;
        RetryAfter = retryAfter;
    }

    /// <summary>Whether the permit was granted.</summary>
    public bool IsAcquired { get; }

    /// <summary>
    /// How long until another attempt is expected to succeed — an estimate, safe to surface to the
    /// requester (see <c>PhoneOtpRateLimiter</c>'s own doc comment for why it's an estimate rather than an
    /// exact figure taken from <see cref="System.Threading.RateLimiting"/> itself). Always
    /// <see cref="TimeSpan.Zero"/> when <see cref="IsAcquired"/> is <see langword="true"/>.
    /// </summary>
    public TimeSpan RetryAfter { get; }

    /// <summary>A denied permit, retryable after roughly <paramref name="retryAfter"/>.</summary>
    public static PhoneOtpAcquireResult Denied(TimeSpan retryAfter) => new(false, retryAfter);
}
