namespace Huia.Tests.Unit.Keys;

/// <summary>
/// Minimal settable <see cref="TimeProvider"/> for deterministic rotation-scheduling tests.
/// </summary>
internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _utcNow = start;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan delta) => _utcNow += delta;
}
