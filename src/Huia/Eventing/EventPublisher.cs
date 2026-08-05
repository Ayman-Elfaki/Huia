using Microsoft.Extensions.DependencyInjection;

namespace Huia.Eventing;

internal sealed class EventPublisher(IServiceProvider serviceProvider) : IEventPublisher
{
    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
    {
        foreach (var handler in serviceProvider.GetServices<IEventHandler<TEvent>>())
        {
            await handler.HandleAsync(@event, cancellationToken).ConfigureAwait(false);
        }
    }
}