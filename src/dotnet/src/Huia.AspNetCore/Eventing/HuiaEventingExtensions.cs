using Huia.AspNetCore.Eventing;
using Huia.Events;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registration helpers for the in-process Huia event pipeline.</summary>
public static class HuiaEventingExtensions
{
    internal static IServiceCollection AddHuiaEventing(this IServiceCollection services)
    {
        services.TryAddSingleton<ChannelHuiaEventPublisher>();
        services.TryAddSingleton<IHuiaEventPublisher>(sp => sp.GetRequiredService<ChannelHuiaEventPublisher>());
        services.AddHostedService<HuiaEventDispatcher>();
        return services;
    }

    /// <summary>Registers a handler for a specific Huia event type.</summary>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <typeparam name="THandler">The handler implementation.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddHuiaEventHandler<TEvent, THandler>(this IServiceCollection services)
        where TEvent : IHuiaEvent
        where THandler : class, IHuiaEventHandler<TEvent>
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IHuiaEventHandler<TEvent>, THandler>();
        return services;
    }
}
