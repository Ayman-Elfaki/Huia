namespace Huia.Events;

/// <summary>
/// Marker for the in-process domain events Huia raises. Implementations are immutable records carrying
/// only primitive, non-sensitive data (identifiers, masked values, timestamps).
/// </summary>
public interface IHuiaEvent
{
    /// <summary>The tenant the event originated in.</summary>
    string TenantId { get; }

    /// <summary>When the event occurred (UTC).</summary>
    DateTimeOffset OccurredAt { get; }
}
