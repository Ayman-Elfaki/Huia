namespace Huia.Eventing;

/// <summary>Publishes Huia domain events to every registered <see cref="IEventHandler{TEvent}"/>.</summary>
public interface IEventPublisher
{
    /// <summary>Invokes every registered <see cref="IEventHandler{TEvent}"/> for <typeparamref name="TEvent"/>, in registration order.</summary>
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default);
}