namespace Huia.Events;

/// <summary>
/// Publishes <see cref="IHuiaEvent"/> instances to in-process subscribers. The default implementation is
/// channel-backed and non-blocking; publishing never throws for subscriber failures.
/// </summary>
public interface IHuiaEventPublisher
{
    /// <summary>Publishes an event to all registered handlers.</summary>
    /// <param name="domainEvent">The event to publish.</param>
    /// <param name="cancellationToken">A token that cancels the enqueue operation.</param>
    /// <returns>A task that completes once the event has been accepted for delivery.</returns>
    ValueTask PublishAsync(IHuiaEvent domainEvent, CancellationToken cancellationToken = default);
}

/// <summary>
/// Consumes a single kind of <see cref="IHuiaEvent"/>. Register implementations in DI; the dispatcher
/// resolves every <see cref="IHuiaEventHandler{TEvent}"/> for the published event type.
/// </summary>
/// <typeparam name="TEvent">The event type this handler consumes.</typeparam>
public interface IHuiaEventHandler<in TEvent>
    where TEvent : IHuiaEvent
{
    /// <summary>Handles the event. Exceptions are logged and swallowed by the dispatcher.</summary>
    /// <param name="domainEvent">The published event.</param>
    /// <param name="cancellationToken">A token tied to the dispatcher's lifetime.</param>
    /// <returns>A task representing the handling operation.</returns>
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);
}
