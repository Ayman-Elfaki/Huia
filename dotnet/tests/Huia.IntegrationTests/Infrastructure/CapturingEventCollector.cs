using System.Collections.Concurrent;
using Huia.Events;

namespace Huia.IntegrationTests.Infrastructure;

/// <summary>Collects every Huia domain event raised during a test.</summary>
public sealed class CapturingEventCollector
{
    private readonly ConcurrentQueue<IHuiaEvent> _events = new();

    /// <summary>Records an event.</summary>
    /// <param name="domainEvent">The event.</param>
    public void Add(IHuiaEvent domainEvent) => _events.Enqueue(domainEvent);

    /// <summary>Every recorded event.</summary>
    public IReadOnlyList<IHuiaEvent> Events => _events.ToArray();

    /// <summary>Waits (polling) until an event of type <typeparamref name="T"/> matching the predicate appears.</summary>
    /// <typeparam name="T">The event type.</typeparam>
    /// <param name="predicate">A filter.</param>
    /// <param name="timeout">How long to wait.</param>
    /// <returns>The matching event.</returns>
    public async Task<T> WaitForAsync<T>(Func<T, bool>? predicate = null, TimeSpan? timeout = null)
        where T : IHuiaEvent
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < deadline)
        {
            var match = _events.OfType<T>().FirstOrDefault(e => predicate is null || predicate(e));
            if (match is not null)
            {
                return match;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"No {typeof(T).Name} event was raised within the timeout.");
    }
}

/// <summary>Forwards one event type into a <see cref="CapturingEventCollector"/>.</summary>
/// <typeparam name="TEvent">The event type.</typeparam>
public sealed class CollectingEventHandler<TEvent>(CapturingEventCollector collector) : IHuiaEventHandler<TEvent>
    where TEvent : IHuiaEvent
{
    /// <inheritdoc />
    public Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken)
    {
        collector.Add(domainEvent);
        return Task.CompletedTask;
    }
}
