using System.Threading.Channels;
using Huia.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Huia.AspNetCore.Eventing;

/// <summary>
/// In-process <see cref="IHuiaEventPublisher"/> backed by an unbounded channel. Publishing only enqueues;
/// a single background reader fans each event out to the registered <see cref="IHuiaEventHandler{TEvent}"/>
/// instances. Handler exceptions are logged and swallowed so one bad subscriber cannot break a sign-in.
/// </summary>
internal sealed class ChannelHuiaEventPublisher : IHuiaEventPublisher
{
    private readonly Channel<IHuiaEvent> _channel = Channel.CreateUnbounded<IHuiaEvent>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
    });

    public ChannelReader<IHuiaEvent> Reader => _channel.Reader;

    public ValueTask PublishAsync(IHuiaEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        return _channel.Writer.WriteAsync(domainEvent, cancellationToken);
    }

    public void Complete() => _channel.Writer.TryComplete();
}

/// <summary>Drains <see cref="ChannelHuiaEventPublisher"/> and dispatches to handlers within a DI scope per event.</summary>
internal sealed partial class HuiaEventDispatcher(
    ChannelHuiaEventPublisher publisher,
    IServiceScopeFactory scopeFactory,
    ILogger<HuiaEventDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var domainEvent in publisher.Reader.ReadAllAsync(stoppingToken))
        {
            await DispatchAsync(domainEvent, stoppingToken);
        }
    }

    private async Task DispatchAsync(IHuiaEvent domainEvent, CancellationToken cancellationToken)
    {
        var handlerType = typeof(IHuiaEventHandler<>).MakeGenericType(domainEvent.GetType());

        await using var scope = scopeFactory.CreateAsyncScope();
        var handlers = scope.ServiceProvider.GetServices(handlerType);

        foreach (var handler in handlers)
        {
            if (handler is null)
            {
                continue;
            }

            try
            {
                var task = (Task)handlerType
                    .GetMethod(nameof(IHuiaEventHandler<IHuiaEvent>.HandleAsync))!
                    .Invoke(handler, [domainEvent, cancellationToken])!;
                await task;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogHandlerFailed(ex, handler.GetType().Name, domainEvent.GetType().Name);
            }
        }
    }

    [LoggerMessage(LogLevel.Error, "Huia event handler {Handler} threw while handling {Event}.")]
    partial void LogHandlerFailed(Exception exception, string handler, string @event);
}
