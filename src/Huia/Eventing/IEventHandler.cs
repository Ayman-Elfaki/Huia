namespace Huia.Eventing;

/// <summary>
/// Reacts to a Huia event (e.g. <see cref="UserSignedInEvent"/>, <see cref="UserRegisteredEvent"/>).
/// Register one or more implementations per event type via DI:
/// <code>services.AddScoped&lt;IHuiaEventHandler&lt;UserRegisteredEvent&gt;, WelcomeEmailHandler&gt;();</code>
/// All registered handlers for an event type run (sequentially) whenever that event is published; an
/// exception from one handler propagates and stops the rest from running.
/// </summary>
public interface IEventHandler<in TEvent>
{
    /// <summary>Handles one occurrence of <typeparamref name="TEvent"/>.</summary>
    Task HandleAsync(TEvent @event, CancellationToken cancellationToken = default);
}