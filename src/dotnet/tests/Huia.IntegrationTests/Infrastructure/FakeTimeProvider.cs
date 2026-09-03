namespace Huia.IntegrationTests.Infrastructure;

/// <summary>
/// A hand-rolled controllable clock. <c>Microsoft.Extensions.TimeProvider.Testing</c> is not on the
/// package list, and the tests only need "now" plus the ability to advance it.
/// </summary>
public sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
{
    private long _ticks = start.UtcTicks;

    public FakeTimeProvider()
        : this(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))
    {
    }

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);

    public override long GetTimestamp() => Interlocked.Read(ref _ticks);

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <summary>Moves the clock forward.</summary>
    /// <param name="delta">How far to advance.</param>
    public void Advance(TimeSpan delta) => Interlocked.Add(ref _ticks, delta.Ticks);
}
