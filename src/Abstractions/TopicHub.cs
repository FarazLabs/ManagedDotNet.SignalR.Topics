using Microsoft.AspNetCore.SignalR;
using ManagedDotNet.SignalR.Topics.Configuration;
using ManagedDotNet.SignalR.Topics.Core;
using ManagedDotNet.SignalR.Topics.Types.Exceptions;

namespace ManagedDotNet.SignalR.Topics.Abstractions;

public abstract class TopicHub : Hub
{
    /// <summary>
    /// Set automatically during instantiation by the DI factory method. <br />
    /// No need to inject or expose this dependency to derived classes.
    /// </summary>
    internal IHubCommandDispatcher Dispatcher { get; set; }

    /// <summary>
    /// Set automatically during instantiation by the DI factory method.
    /// </summary>
    internal EndpointOptionRegistry Registry { get; set; }

    /// <summary>
    /// Client selectors stamped with this hub's type for outbound topic routing.
    /// </summary>
    public new TopicHubCallerClients Clients => new TopicHubCallerClients(base.Clients, GetType(), Registry);

    /// <summary>
    /// Invoked by the client to process a message routed by topic.
    /// </summary>
    /// <param name="topic">The message topic used for routing to the appropriate handler.</param>
    /// <param name="message">The serialized message payload.</param>
    /// <exception cref="ServiceNotRegisteredException">
    /// Thrown when no handler is registered for the resolved handler type.
    /// </exception>
    // ponytail: ConnectionAborted only — hub CancellationToken is not synthetic on net8 non-stream methods (would break 2-arg wire). Link InvokeAsync cancel when targeting net11+.
    public Task Handle(string topic, string message) =>
        Dispatcher.DispatchAsync(GetType(), topic, message, Context, Context.ConnectionAborted);
}
